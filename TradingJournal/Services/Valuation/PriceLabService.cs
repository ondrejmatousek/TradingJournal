using TradingJournal.Services.Screening;

namespace TradingJournal.Services.Valuation;

public sealed class PriceLabService(ScreeningStore store, InvestmentCatalog catalog, IValuationEngine engine)
{
    // Profitable nonfinancial businesses, checked against the available financial datasets.
    public static IReadOnlyList<string> PilotSymbols { get; } = Array.AsReadOnly(new[] { "MSFT", "META", "AAPL", "CTAS", "RSG" });
    private static string Key(string user, string symbol) => "price-lab-" + user + "-" + symbol;

    public async Task<PriceLabResult> AnalyseAsync(string user, string symbol, CancellationToken ct, PriceLabSettings? supplied = null)
    {
        var stock = (await catalog.AllAsync(ct)).SingleOrDefault(s => s.Symbol == symbol)
            ?? throw new ArgumentException("Neznámý ticker.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var settings = supplied ?? await store.ReadAsync<PriceLabSettings>(Key(user, symbol), ct) ?? new();
        var validation = settings.Validate(today);
        if (validation != null) throw new ArgumentException(validation);
        var cache = await store.ReadAsync<ScreeningMarketCache>("market-" + symbol, ct);
        var data = PriceLabMath.SelectFinancials(cache?.Financials ?? cache?.Fundamentals?.ValuationFinancials, today);
        var price = cache?.Prices.Where(p => p.Date <= today && p.Close > 0).OrderBy(p => p.Date).LastOrDefault();
        var macro = await store.ReadAsync<List<MacroObservation>>("macro-v1", ct) ?? [];
        var rf = macro.FirstOrDefault(m => m.Series == "DGS10");
        Assumption? riskFree = rf == null ? null : new("RiskFree", rf.Value / 100, "ratio", rf.Source, rf.Date, false, "US Treasury 10Y");
        ValuationReport Calculate(ValuationForecast? forecast) => engine.Calculate(symbol, stock.Sector, data, price?.Close, price?.Date, today, riskFree, forecast);
        var report = Calculate(settings.Forecast);
        var basis = report.Scenarios.FirstOrDefault(s => s.Name == "Base");
        var notes = new List<string>
        {
            "Cenové pásmo tvoří scénáře DCF; nejde o statistický interval spolehlivosti.",
            "Limit pro požadovaný výnos vychází z FCFE při konstantním nominálním dluhu, jeho refinancování a konstantních nákladech dluhu. Nejde o garantované IRR ani předpověď burzovní ceny.",
            "Růst a marže jsou bez vlastního zdrojovaného výhledu extrapolací historie. Automatický konsenzus analytiků ani sektorové srovnání zatím nejsou připojené."
        };
        var use = basis != null && report.DataQuality >= 60;
        notes.AddRange(cache?.Issues.Select(i => i.Message) ?? []);
        var returnPrice = use ? PriceLabMath.ReturnPrice(report, basis!, settings.RequiredReturnPercent / 100) : null;
        var conservative = returnPrice * (1 - report.MarginOfSafety);
        var multiple = settings.ForwardEps * settings.ReferencePe;
        var disagreement = multiple != null && basis != null && Math.Abs(multiple.Value / basis.FairValue - 1) > .30m;
        if (disagreement) notes.Add("DCF a zvolené EPS × P/E se rozcházejí o více než 30 %. Prověř předpoklady; hodnoty se automaticky neprůměrují.");
        if (!use) notes.Add("Pro nákupní limit chybí platné ocenění s kvalitou podkladů alespoň 60/100. Aktualizuj data nebo zkontroluj výkazy.");
        if (use && returnPrice == null) notes.Add("Limit nelze určit: požadovaný výnos musí převyšovat terminální růst a výsledná hodnota vlastního kapitálu musí být kladná.");
        if (basis is { EnterpriseValue: > 0 } && basis.PvTerminal / basis.EnterpriseValue > .75m)
            notes.Add("Více než 75 % hodnoty podniku pochází z terminální hodnoty; výsledek je zvlášť citlivý na dlouhodobé předpoklady.");
        var reverse = use && price != null ? PriceLabMath.Reverse(price.Close, growth =>
        {
            ct.ThrowIfCancellationRequested();
            var forecast = new ValuationForecast(growth, settings.MarginPercent / 100, settings.Beta, "ReverseDCF numerical scenario", today);
            return Calculate(forecast).Scenarios.FirstOrDefault(s => s.Name == "Base");
        }, ct) : new ReverseDcfResult("unavailable", null, null, "Chybí platné ocenění a aktuální cena pro reverzní DCF.");
        return new(symbol, stock.Name, price?.Close, price?.Date, data?.Latest?.End, settings, report,
            returnPrice, conservative, multiple, disagreement || !use, reverse, notes)
        {
            FinancialSources = data?.Latest?.Facts.Values.Select(f => f.Source).Distinct().ToList() ?? [],
            LatestFiled = data?.Latest?.Facts.Values.Select(f => (DateOnly?)f.Filed).Max()
        };
    }

    public Task SaveAsync(string user, string symbol, PriceLabSettings settings, CancellationToken ct)
        => store.WriteAsync(Key(user, symbol), settings, ct);
}
