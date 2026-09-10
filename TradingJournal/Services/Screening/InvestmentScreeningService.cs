using Microsoft.EntityFrameworkCore;
using TradingJournal.Services.Valuation;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services;

namespace TradingJournal.Services.Screening;

public sealed class InvestmentScreeningService(ApplicationDbContext db, ScreeningStore store, ScreeningDataProvider provider, IUnderEma200HistoryProvider history, TursoSeasonalityStore? turso = null, IValuationEngine? engine = null, ValuationPersistence? valuations = null, InvestmentCatalog? catalog = null, ILogger<InvestmentScreeningService>? logger = null)
{
    private static readonly SemaphoreSlim RefreshGate = new(3, 3);
    public async Task<List<ScreeningRow>> LoadAsync(string user, CancellationToken ct)
        => await LoadAsync(user, ct, null);

    public async Task<List<ScreeningRow>> LoadAsync(string user, CancellationToken ct, IReadOnlyCollection<string>? requestedSymbols)
    {
        var universe = await UniverseAsync(ct);
        var stocks = requestedSymbols is null
            ? universe
            : universe.Where(s => requestedSymbols.Contains(s.Symbol, StringComparer.OrdinalIgnoreCase)).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var macro = await ContextAsync(ct);
        var rows = new List<ScreeningRow>();
        foreach (var stock in stocks)
        {
            var cache = await store.ReadAsync<ScreeningMarketCache>("market-" + stock.Symbol, ct);
            var research = await store.ReadAsync<ScreeningResearch>("research-" + user + "-" + stock.Symbol, ct) ?? new();
            var prices = Merge(await LoadRecentAsync(stock.Symbol, today, ct), Recent(cache?.Prices ?? [], today));
            var persisted = research.Fundamentals == null ? cache?.Valuation : null;
            if (engine != null && persisted?.Model != "ValuationV2.0") persisted = null;
            var row = ScreeningCalculator.Evaluate(stock, prices, cache?.Fundamentals, macro, research, DateOnly.FromDateTime(DateTime.UtcNow), persisted, allowLegacyCalculation: engine == null);
            row.RefreshMessage = string.Join(" ", new[] { cache?.Message, cache?.ValuationFailure });
            row.ValuationRunId = cache?.ValuationRunId;
            if (engine != null && research.Fundamentals != null) row.Risks.Add("Vlastní starší formát výkazů není plným auditovaným V2 vstupem; ocenění je pozastavené.");
            rows.Add(row);
        }
        return rows;
    }

    public async Task<ScreeningMarketCache> RefreshMarketAsync(string symbol, CancellationToken ct, bool force = false)
    {
        var stock = (await UniverseAsync(ct)).Single(s => s.Symbol == symbol);
        await RefreshGate.WaitAsync(ct);
        try
        {
            var old = await store.ReadAsync<ScreeningMarketCache>("market-" + symbol, ct);
            var messages = new List<string>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var prices = Recent(old?.Prices ?? [], today);
            FundamentalInput? fundamental = old?.Fundamentals;
            var financials = old?.Financials ?? fundamental?.ValuationFinancials;
            // Explicit user refresh bypasses the local result cache; provider rate limits still apply.
            if (force || old == null || (engine != null && financials == null) || DateTimeOffset.UtcNow - old.RetrievedAt > (string.IsNullOrWhiteSpace(old.Message) ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(15)))
            {
                try
                {
                    var fetched = await history.GetDailyPricesAsync(symbol, ct);
                    if (fetched.Count > 0) prices = Recent(fetched.Select(p => new SeasonalityPrice(DateOnly.FromDateTime(p.Date), p.Close)), today);
                    else messages.Add("Cenový poskytovatel nevrátil historii; zachována dostupná data.");
                }
                catch (Exception) when (!ct.IsCancellationRequested) { messages.Add("Ceny se nepodařilo aktualizovat."); }
                try
                {
                    var fetched = await provider.CompanyFinancialsAsync(symbol, ct);
                    if (fetched.Financials?.Latest != null) financials = fetched.Financials;
                    if (fetched.Fundamentals != null) fundamental = fetched.Fundamentals;
                    else messages.Add("SEC neposkytla úplné srovnatelné roční výkazy v USD; předchozí výkazy zůstaly zachované.");
                }
                catch (Exception) when (!ct.IsCancellationRequested) { messages.Add("Stažení výkazů SEC selhalo nebo platí dočasný limit poskytovatele; předchozí výkazy zůstaly zachované."); }
                var updated = new ScreeningMarketCache(DateTimeOffset.UtcNow, prices, fundamental == null ? null : fundamental with { ValuationFinancials = null }, string.Join(" ", messages))
                    { Financials = financials };
                if (engine != null)
                {
                    var macro = await store.ReadAsync<List<MacroObservation>>("macro-v1", ct) ?? [];
                    var rf = macro.FirstOrDefault(x=>x.Series=="DGS10");
                    var lastPrice=prices.OrderBy(x=>x.Date).LastOrDefault();
                    var report=engine.Calculate(symbol, stock.Sector, financials, lastPrice?.Close, lastPrice?.Date, today,
                        rf==null?null:new Assumption("RiskFree",rf.Value/100,"ratio",rf.Source,rf.Date,false,"US Treasury 10Y"));
                    if(valuations != null) await valuations.SaveAsync(report,financials,ct);
                    updated=updated with { Valuation=ValuationRegistration.Summary(report), ValuationRunId=report.Id,
                        ValuationFailure=report.BaseFairValue==null?string.Join(" ",report.Warnings):null };
                    logger?.LogInformation("Valuation {Ticker} {Strategy} {Version} quality {Quality} uncertainty {Uncertainty}: base {Base}, status {Status}",symbol,report.Strategy,report.ModelVersion,report.DataQuality,report.Uncertainty,report.BaseFairValue,report.Status);
                }
                await store.WriteAsync("market-" + symbol, updated, ct);
                return updated;
            }
            else if (old.Message != null) messages.Add(old.Message);
            return old ?? new ScreeningMarketCache(DateTimeOffset.UtcNow, prices, fundamental, string.Join(" ", messages));
        }
        finally { RefreshGate.Release(); }
    }

    public async Task<ScreeningRow> RefreshAsync(string symbol, string user, CancellationToken ct, bool force = false)
    {
        var stock = (await UniverseAsync(ct)).Single(s => s.Symbol == symbol);
        var market = await RefreshMarketAsync(symbol, ct, force);
        var seasonal = await LoadRecentAsync(symbol, DateOnly.FromDateTime(DateTime.UtcNow), ct);
        var research = await store.ReadAsync<ScreeningResearch>("research-" + user + "-" + symbol, ct) ?? new();
        var macro = await ContextAsync(ct);
        var result = ScreeningCalculator.Evaluate(stock, Merge(seasonal, market.Prices), market.Fundamentals, macro, research, DateOnly.FromDateTime(DateTime.UtcNow), market.Valuation, allowLegacyCalculation: engine == null);
        result.RefreshMessage = string.Join(" ", new[]{market.Message, market.ValuationFailure});
        result.ValuationRunId = market.ValuationRunId;
        return result;
    }

    public Task<IReadOnlyList<InvestmentSymbol>> UniverseAsync(CancellationToken ct) => catalog?.AllAsync(ct) ?? Task.FromResult(InvestmentUniverse.All);

    private static IReadOnlyList<SeasonalityPrice> Merge(IReadOnlyList<SeasonalityPrice> seasonal, IReadOnlyList<SeasonalityPrice> market)
    {
        // Never splice price levels from different providers: split adjustments may differ.
        if (market.Count == 0) return seasonal;
        if (seasonal.Count == 0) return market;
        return market.Max(p => p.Date) >= seasonal.Max(p => p.Date) ? market : seasonal;
    }

    private async Task<IReadOnlyList<SeasonalityPrice>> LoadRecentAsync(string symbol, DateOnly today, CancellationToken ct)
    {
        if (turso?.IsConfigured == true) return await turso.LoadRecentAsync(symbol, today, ct);
        var prices = await db.SeasonalityPrices.AsNoTracking().Where(p => p.AssetKey == symbol && p.Date <= today)
            .OrderByDescending(p => p.Date).Take(252).Select(p => new SeasonalityPrice(p.Date, p.Close)).ToListAsync(ct);
        prices.Reverse();
        return prices;
    }

    private static List<SeasonalityPrice> Recent(IEnumerable<SeasonalityPrice> prices, DateOnly today)
        => prices.Where(p => p.Date <= today && p.Close > 0).DistinctBy(p => p.Date).OrderBy(p => p.Date).TakeLast(252).ToList();

    private async Task<List<MacroObservation>> ContextAsync(CancellationToken ct)
    {
        var macro = await store.ReadAsync<List<MacroObservation>>("macro-v1", ct) ?? [];
        var geo = await store.ReadAsync<MacroObservation>("macro-geopolitics-v1", ct);
        if (geo != null) macro.Add(geo);
        return macro;
    }
}


