using TradingJournal.Services.Valuation;

namespace TradingJournal.Services.Screening;

public sealed record DataIssue(string Code, string Source, string Message)
{
    public static DataIssue FromException(string source, Exception ex) => ex switch
    {
        HttpRequestException http => new("api", source, $"{source}: HTTP {(http.StatusCode is {} status ? ((int)status).ToString() : "nedostupné spojení")}."),
        OperationCanceledException => new("timeout", source, $"{source}: vypršel časový limit."),
        System.Text.Json.JsonException => new("invalid", source, $"{source}: neplatný formát odpovědi."),
        InvalidOperationException => new("cooldown", source, $"{source}: dočasně pozastaveno po chybě poskytovatele."),
        _ => new("invalid", source, $"{source}: neplatné údaje v odpovědi.")
    };
}

public sealed record FinancialFetchResult(FundamentalInput? Fundamentals, FinancialDataset? Financials, List<DataIssue> Issues);

public static class FinancialDataMerge
{
    public static readonly string[] Required = ["Revenue", "Ebit", "Da", "CapEx", "Cash"];
    public static bool HasValuationInputs(FinancialDataset? data) => data?.Latest is {} p
        && Required.All(p.Facts.ContainsKey) && ValuationMath.Debt(p) != null
        && (data.ActualShares != null || p.Facts.ContainsKey("DilutedShares") || p.Facts.ContainsKey("BasicShares"))
        && data.History.Count(h => h.Facts.ContainsKey("Revenue") && h.Facts.ContainsKey("Ebit")) >= 3;

    // Facts are supplemented only within the same fiscal period and currency.
    // Existing primary facts (including zero) always win; never splice TTM with FY.
    public static FinancialFetchResult Combine(FinancialFetchResult primary, FinancialFetchResult secondary)
    {
        var issues = primary.Issues.Concat(secondary.Issues).ToList();
        var a = primary.Financials; var b = secondary.Financials;
        if (a?.Latest == null) return secondary with { Issues = issues, Financials = b ?? a, Fundamentals = secondary.Fundamentals ?? primary.Fundamentals };
        if (b?.Latest == null) return primary with { Issues = issues };
        if (a.Currency != b.Currency)
        {
            issues.Add(new("currency", "Merge", $"Výkazy {a.Currency} a {b.Currency} nelze sloučit bez převodu měny."));
            return primary with { Issues = issues };
        }
        FinancialPeriod MergePeriod(FinancialPeriod p)
        {
            var match = b.History.Append(b.Latest).FirstOrDefault(q => q.Start == p.Start && q.End == p.End && q.IsTtm == p.IsTtm);
            if (match == null) return p;
            var facts = new Dictionary<string, FinancialFact>(p.Facts);
            foreach (var (key, fact) in match.Facts)
            {
                if (fact.Unit != (key.EndsWith("Shares") ? "shares" : a.Currency)) continue;
                // Debt disclosures from different sources may overlap. Keep one source's debt group.
                if (key.Contains("Debt") || key == "CommercialPaper")
                    if (p.Facts.Keys.Any(k => k.Contains("Debt") || k == "CommercialPaper")) continue;
                facts.TryAdd(key, fact);
            }
            return p with { Facts = facts };
        }
        var merged = a with { Latest = MergePeriod(a.Latest), History = a.History.Select(MergePeriod).ToList(),
            Warnings = a.Warnings.Concat(b.Warnings).Distinct().ToList() };
        if (merged.Latest.Facts.Count == a.Latest.Facts.Count && !HasValuationInputs(a) && HasValuationInputs(b)
            && b.Latest.End >= a.Latest.End)
        {
            // A complete secondary dataset can replace an incomplete primary dataset as a whole.
            return secondary with { Issues = issues };
        }
        var fundamentals = primary.Fundamentals ?? FromDataset(merged);
        return new(fundamentals, merged, issues);
    }

    public static FundamentalInput? FromDataset(FinancialDataset data)
    {
        var p = data.History.Where(x => !x.IsTtm).OrderBy(x => x.End).LastOrDefault();
        if (p == null) return null;
        var previous = data.History.Where(x => !x.IsTtm && p.End.DayNumber - x.End.DayNumber is >= 330 and <= 400)
            .OrderByDescending(x => x.End).FirstOrDefault();
        string[] required = ["Revenue", "Ocf", "CapEx", "NetIncome", "Assets", "Liabilities"];
        if (previous == null || !required.All(p.Facts.ContainsKey) || !previous.Facts.ContainsKey("Revenue")) return null;
        var shares = data.ActualShares?.Value ?? p.Facts.GetValueOrDefault("DilutedShares")?.Value;
        if (shares is not > 0) return null;
        decimal V(string key) => p.Facts[key].Value;
        return new(p.End, p.Facts.Values.Max(f => f.Filed), V("Revenue"), previous.Facts["Revenue"].Value,
            V("Ocf"), V("CapEx"), V("NetIncome"), V("Assets"), V("Liabilities"), shares.Value,
            string.Join("; ", p.Facts.Values.Select(f => f.Source).Distinct()), data.Currency)
        { CashAndEquivalents = p.Facts.GetValueOrDefault("Cash")?.Value, TotalDebt = ValuationMath.Debt(p), ValuationFinancials = data };
    }
}
