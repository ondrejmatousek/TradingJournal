using TradingJournal.Models;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Services.Screening;

public sealed record CoverageCheck(string Key, string Label, string Status, string Detail, string? Source = null, string? Date = null);
public sealed record CoverageAudit(string Symbol, DateTimeOffset? RetrievedAt, List<CoverageCheck> Checks, List<DataIssue> Issues)
{
    public bool Ready => Checks.All(c => c.Status == "available");
}

public static class ScreeningCoverage
{
    public static CoverageAudit Assess(string symbol, IReadOnlyList<MarketPrice> prices, FundamentalInput? fundamentals,
        FinancialDataset? financials, ValuationResult? valuation, DateOnly today, DateTimeOffset? retrievedAt = null,
        IEnumerable<DataIssue>? issues = null, string? valuationFailure = null, string? priceSource = null, string? valuationStatus = null)
    {
        var valid = prices.Where(p => p.Close > 0 && p.Date <= today).DistinctBy(p => p.Date).OrderBy(p => p.Date).TakeLast(252).ToArray();
        var latest = valid.LastOrDefault();
        var fresh = latest != null && today.DayNumber - latest.Date.DayNumber <= 7;
        var continuous = valid.Length == 252 && !valid.Zip(valid.Skip(1)).Any(p => p.Second.Date.DayNumber - p.First.Date.DayNumber > 10);
        var p = financials?.Latest;
        var missing = p == null ? new List<string> { "výkazy pro ocenění" } : FinancialDataMerge.Required.Where(k => !p.Facts.ContainsKey(k)).ToList();
        if (p != null)
        {
            if (ValuationMath.Debt(p) == null) missing.Add("úplné zadlužení");
            if (financials!.ActualShares == null && !p.Facts.ContainsKey("DilutedShares") && !p.Facts.ContainsKey("BasicShares")) missing.Add("počet akcií");
            if (financials.History.Count(h => h.Facts.ContainsKey("Revenue") && h.Facts.ContainsKey("Ebit")) < 3) missing.Add("3 roky tržeb a EBIT");
        }
        if (fundamentals == null) missing.Add("srovnatelné fundamenty pro kvalitu firmy");
        var currency = financials?.Currency ?? fundamentals?.Currency;
        var stale = p != null && (p.End > today || today.DayNumber - p.End.DayNumber > 460 || p.Facts.Values.Any(f => f.Filed > today || f.End > today || f.Start > f.End));
        var fundamentalsValid = fundamentals != null && fundamentals.Currency == "USD" && fundamentals.PeriodEnd <= today
            && today.DayNumber - fundamentals.PeriodEnd.DayNumber <= 460 && fundamentals.Filed <= today && fundamentals.Filed >= fundamentals.PeriodEnd;
        if (fundamentals != null && !fundamentalsValid) missing.Add("aktuální srovnatelné fundamenty");
        var checks = new List<CoverageCheck>
        {
            new("price", "Cena", fresh ? "available" : latest == null ? "missing" : "stale", fresh ? "Aktuální závěrečná cena." : "Chybí cena z posledních 7 dní.", priceSource ?? "Původní zdroj starší uložené historie není zaznamenaný.", latest?.Date.ToString("yyyy-MM-dd")),
            new("history", "Historie", continuous && fresh ? "available" : "missing", $"{valid.Length}/252 závěrečných cen; {(continuous ? "bez mezery nad 10 dní" : "chybí souvislá historie")}."),
            new("financials", "Výkazy", currency is not null and not "USD" ? "unsupported" : stale ? "stale" : missing.Count == 0 ? "available" : "missing",
                currency is not null and not "USD" ? $"Nepodporovaná měna {currency}." : stale ? "Období výkazů je zastaralé nebo budoucí." : missing.Count == 0 ? "Základní vstupy dostupné; podrobnou validaci provádí ocenění." : "Chybí: " + string.Join(", ", missing),
                p == null ? fundamentals?.Source : string.Join("; ", p.Facts.Values.Select(f => f.Source).Distinct()), (p?.End ?? fundamentals?.PeriodEnd)?.ToString("yyyy-MM-dd")),
            new("valuation", "Ocenění", valuationStatus == "UnsupportedCompanyType" || currency is not null and not "USD" ? "unsupported" : valuation?.Base > 0 && valuation.Confidence >= 60 && fresh && !stale && fundamentalsValid ? "available" : "missing",
                valuation?.Base > 0 ? $"Důvěryhodnost {valuation.Confidence:0}/100; pro závěr minimum 60 a aktuální podklady." : valuationFailure ?? "Ocenění dosud nevzniklo; aktualizovat akcii.")
        };
        return new(symbol, retrievedAt, checks, issues?.ToList() ?? []);
    }

    public static string Decision(ScreeningRow row) => row.Entry?.Status switch
    {
        "candidate" => "Kandidát k prověření",
        "risk" => "Zvýšené riziko",
        "expensive" or "wait" => "Čekat na cenu",
        _ => "Chybí podklady"
    };
}
