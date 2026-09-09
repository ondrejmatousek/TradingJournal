using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services;

namespace TradingJournal.Services.Screening;

public sealed class InvestmentScreeningService(ApplicationDbContext db, ScreeningStore store, ScreeningDataProvider provider, IUnderEma200HistoryProvider history, TursoSeasonalityStore? turso = null)
{
    private static readonly SemaphoreSlim RefreshGate = new(3, 3);
    public async Task<List<ScreeningRow>> LoadAsync(string user, CancellationToken ct)
    {
        var keys = InvestmentUniverse.All.Select(s => s.Symbol).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var historyStart = today.AddYears(-15);
        Dictionary<string, IReadOnlyList<SeasonalityPrice>> bySymbol;
        if (turso?.IsConfigured == true)
            bySymbol = (await turso.LoadAllAsync(ct)).Where(x => keys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => (IReadOnlyList<SeasonalityPrice>)x.Value.Where(p => p.Date >= historyStart && p.Date <= today).ToArray(), StringComparer.OrdinalIgnoreCase);
        else
        {
            var cachedPrices = await db.SeasonalityPrices.AsNoTracking().Where(p => keys.Contains(p.AssetKey) && p.Date >= historyStart && p.Date <= today).OrderBy(p => p.AssetKey).ThenBy(p => p.Date).Select(p => new { p.AssetKey, p.Date, p.Close }).ToListAsync(ct);
            bySymbol = cachedPrices.GroupBy(p => p.AssetKey).ToDictionary(g => g.Key, g => (IReadOnlyList<SeasonalityPrice>)g.Select(p => new SeasonalityPrice(p.Date, p.Close)).ToArray());
        }
        var macro = await ContextAsync(ct);
        var rows = new List<ScreeningRow>();
        foreach (var stock in InvestmentUniverse.All)
        {
            var cache = await store.ReadAsync<ScreeningMarketCache>("market-" + stock.Symbol, ct);
            var research = await store.ReadAsync<ScreeningResearch>("research-" + user + "-" + stock.Symbol, ct) ?? new();
            var prices = Merge(bySymbol.GetValueOrDefault(stock.Symbol, []), cache?.Prices ?? []);
            var row = ScreeningCalculator.Evaluate(stock, prices, cache?.Fundamentals, macro, research, DateOnly.FromDateTime(DateTime.UtcNow));
            row.RefreshMessage = cache?.Message;
            rows.Add(row);
        }
        return rows;
    }

    public async Task<ScreeningMarketCache> RefreshMarketAsync(string symbol, CancellationToken ct, bool force = false)
    {
        _ = InvestmentUniverse.All.Single(s => s.Symbol == symbol);
        await RefreshGate.WaitAsync(ct);
        try
        {
            var old = await store.ReadAsync<ScreeningMarketCache>("market-" + symbol, ct);
            var messages = new List<string>();
            var prices = old?.Prices ?? [];
            FundamentalInput? fundamental = old?.Fundamentals;
            // Explicit user refresh bypasses the local result cache; provider rate limits still apply.
            if (force || old == null || DateTimeOffset.UtcNow - old.RetrievedAt > (string.IsNullOrWhiteSpace(old.Message) ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(15)))
            {
                try
                {
                    var fetched = await history.GetDailyPricesAsync(symbol, ct);
                    if (fetched.Count > 0) prices = fetched.Select(p => new SeasonalityPrice(DateOnly.FromDateTime(p.Date), p.Close)).ToList();
                    else messages.Add("Cenový poskytovatel nevrátil historii; zachována dostupná data.");
                }
                catch (Exception) when (!ct.IsCancellationRequested) { messages.Add("Ceny se nepodařilo aktualizovat."); }
                try
                {
                    var f = await provider.FundamentalsAsync(symbol, ct);
                    if (f != null) fundamental = f;
                    else messages.Add("SEC neposkytla úplné srovnatelné roční výkazy v USD; předchozí výkazy zůstaly zachované.");
                }
                catch (Exception) when (!ct.IsCancellationRequested) { messages.Add("Stažení výkazů SEC selhalo nebo platí dočasný limit poskytovatele; předchozí výkazy zůstaly zachované."); }
                await store.WriteAsync("market-" + symbol, new ScreeningMarketCache(DateTimeOffset.UtcNow, prices, fundamental, string.Join(" ", messages)), ct);
            }
            else if (old.Message != null) messages.Add(old.Message);
            return new ScreeningMarketCache(DateTimeOffset.UtcNow, prices, fundamental, string.Join(" ", messages));
        }
        finally { RefreshGate.Release(); }
    }

    public async Task<ScreeningRow> RefreshAsync(string symbol, string user, CancellationToken ct, bool force = false)
    {
        var stock = InvestmentUniverse.All.Single(s => s.Symbol == symbol);
        var market = await RefreshMarketAsync(symbol, ct, force);
        var seasonal = turso?.IsConfigured == true ? await turso.LoadAsync(symbol, ct) : await db.SeasonalityPrices.AsNoTracking().Where(p => p.AssetKey == symbol).OrderBy(p => p.Date).Select(p => new SeasonalityPrice(p.Date, p.Close)).ToListAsync(ct);
        var research = await store.ReadAsync<ScreeningResearch>("research-" + user + "-" + symbol, ct) ?? new();
        var macro = await ContextAsync(ct);
        var result = ScreeningCalculator.Evaluate(stock, Merge(seasonal, market.Prices), market.Fundamentals, macro, research, DateOnly.FromDateTime(DateTime.UtcNow));
        result.RefreshMessage = market.Message;
        return result;
    }

    private static IReadOnlyList<SeasonalityPrice> Merge(IReadOnlyList<SeasonalityPrice> seasonal, IReadOnlyList<SeasonalityPrice> market)
    {
        // Never splice price levels from different providers: split adjustments may differ.
        if (market.Count == 0) return seasonal;
        if (seasonal.Count == 0) return market;
        return market.Max(p => p.Date) >= seasonal.Max(p => p.Date) ? market : seasonal;
    }

    private async Task<List<MacroObservation>> ContextAsync(CancellationToken ct)
    {
        var macro = await store.ReadAsync<List<MacroObservation>>("macro-v1", ct) ?? [];
        var geo = await store.ReadAsync<MacroObservation>("macro-geopolitics-v1", ct);
        if (geo != null) macro.Add(geo);
        return macro;
    }
}


