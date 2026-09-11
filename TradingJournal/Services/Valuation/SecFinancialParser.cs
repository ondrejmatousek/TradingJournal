using System.Text.Json;

namespace TradingJournal.Services.Valuation;

// SEC units are absolute USD/shares, not the display scale of an HTML statement.
public static class SecFinancialParser
{
    private static readonly Dictionary<string, string[]> Tags = new()
    {
        ["Revenue"] = ["RevenueFromContractWithCustomerExcludingAssessedTax", "Revenues", "SalesRevenueNet"],
        ["Ebit"] = ["OperatingIncomeLoss"], ["Tax"] = ["IncomeTaxExpenseBenefit"],
        ["Pretax"] = ["IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest", "IncomeLossFromContinuingOperationsBeforeIncomeTaxesMinorityInterestAndIncomeLossFromEquityMethodInvestments"],
        ["Da"] = ["DepreciationDepletionAndAmortization", "DepreciationDepletionAndAmortizationPropertyPlantAndEquipment"],
        ["CapEx"] = ["PaymentsToAcquirePropertyPlantAndEquipment"], ["Ocf"] = ["NetCashProvidedByUsedInOperatingActivities"],
        ["NetIncome"] = ["NetIncomeLoss"], ["Interest"] = ["InterestExpenseNonOperating", "InterestAndDebtExpense", "InterestExpense"],
        ["Sbc"] = ["ShareBasedCompensation"], ["BasicShares"] = ["WeightedAverageNumberOfSharesOutstandingBasic"],
        ["DilutedShares"] = ["WeightedAverageNumberOfDilutedSharesOutstanding"],
        ["Cash"] = ["CashAndCashEquivalentsAtCarryingValue"],
        ["ShortInvestments"] = ["ShortTermInvestments", "MarketableSecuritiesCurrent"],
        ["LongInvestments"] = ["MarketableSecuritiesNoncurrent", "LongTermInvestments"],
        ["Receivables"] = ["AccountsReceivableNetCurrent"], ["Inventory"] = ["InventoryNet"],
        ["Payables"] = ["AccountsPayableCurrent"], ["OtherOperatingAssets"] = ["OtherAssetsCurrent"],
        ["OtherOperatingLiabilities"] = ["OtherLiabilitiesCurrent"],
        ["DebtAggregate"] = ["LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities", "LongTermDebtAndFinanceLeaseObligationsIncludingCurrentMaturities", "LongTermDebt"],
        ["LongDebt"] = ["LongTermDebtNoncurrent", "LongTermDebtAndFinanceLeaseObligationsNoncurrent"],
        ["CurrentDebt"] = ["LongTermDebtCurrent", "LongTermDebtAndFinanceLeaseObligationsCurrent"],
        ["ShortDebt"] = ["ShortTermBorrowings"], ["CommercialPaper"] = ["CommercialPaper"],
        ["Lease"] = ["OperatingLeaseLiabilityNoncurrent"], ["LeaseCurrent"] = ["OperatingLeaseLiabilityCurrent"]
    };
    private static readonly HashSet<string> Durations = ["Revenue", "Ebit", "Tax", "Pretax", "Da", "CapEx", "Ocf", "NetIncome", "Interest", "Sbc", "BasicShares", "DilutedShares"];

    public static FinancialDataset Parse(JsonElement root, string source, DateOnly today)
    {
        var warnings = new List<string>();
        if (!root.TryGetProperty("facts", out var facts) || !facts.TryGetProperty("us-gaap", out var gaap))
            return new("USD", today, [], null, null, ["Unsupported taxonomy: SEC us-gaap USD facts unavailable."]);
        List<FinancialFact> Read(JsonElement taxonomy, string unit, string[] tags)
        {
            var list = new List<FinancialFact>();
            foreach (var tag in tags)
            {
                if (!taxonomy.TryGetProperty(tag, out var metric) || !metric.TryGetProperty("units", out var units) || !units.TryGetProperty(unit, out var values)) continue;
                foreach (var row in values.EnumerateArray())
                {
                    if (!row.TryGetProperty("val", out var val) || !val.TryGetDecimal(out var value)
                        || !row.TryGetProperty("end", out var end) || !row.TryGetProperty("filed", out var filed)) continue;
                    var e = DateOnly.Parse(end.GetString()!); var f = DateOnly.Parse(filed.GetString()!);
                    var start = row.TryGetProperty("start", out var s) ? DateOnly.Parse(s.GetString()!) : e;
                    if (e > today || f > today || start > e) continue;
                    list.Add(new(value, unit, start, e, f, source, tag, row.TryGetProperty("accn", out var a) ? a.GetString()! : ""));
                }
            }
            // Prefer latest available restatement, then taxonomy priority; never sum duplicate facts.
            return list.OrderByDescending(x => x.Filed).ThenBy(x => Array.IndexOf(tags, x.Tag)).ToList();
        }
        var all = Tags.ToDictionary(k => k.Key, k => Read(gaap, k.Key.EndsWith("Shares") ? "shares" : "USD", k.Value));
        FinancialFact? Exact(string key, DateOnly start, DateOnly end) => all[key].FirstOrDefault(f => f.Start == start && f.End == end);
        FinancialPeriod Period(DateOnly start, DateOnly end)
        {
            var values = new Dictionary<string, FinancialFact>();
            foreach (var key in Tags.Keys)
            {
                var f = Exact(key, Durations.Contains(key) ? start : end, end);
                if (f != null) values[key] = f;
            }
            return new(start, end, false, values);
        }
        var revenue = all["Revenue"].Where(f => f.End.DayNumber - f.Start.DayNumber is >= 330 and <= 380)
            .DistinctBy(f => f.End).OrderByDescending(f => f.End).Take(11).ToArray();
        var annual = revenue.Select(f => Period(f.Start, f.End)).OrderBy(p => p.End).ToList();
        var latest = annual.LastOrDefault();
        if (latest != null)
        {
            // TTM = last FY + current YTD - comparable prior YTD (cash flow filings are cumulative).
            var ytd = all["Revenue"].Where(f => f.End > latest.End && f.Start == latest.End.AddDays(1) && f.End.DayNumber - f.Start.DayNumber is >= 60 and <= 300)
                .OrderByDescending(f => f.End).FirstOrDefault();
            if (ytd != null)
            {
                var prior = all["Revenue"].Where(f => f.Start == latest.Start && Math.Abs((ytd.End.DayNumber-ytd.Start.DayNumber) - (f.End.DayNumber-f.Start.DayNumber)) <= 7)
                    .OrderByDescending(f => f.Filed).FirstOrDefault();
                if (prior != null)
                {
                    var result = new Dictionary<string, FinancialFact>();
                    foreach (var key in Tags.Keys)
                    {
                        if (!Durations.Contains(key)) { var instant = Exact(key, ytd.End, ytd.End); if (instant != null) result[key] = instant; continue; }
                        var a = latest.Facts.GetValueOrDefault(key); var b = Exact(key, ytd.Start, ytd.End); var c = Exact(key, prior.Start, prior.End);
                        if (a == null || b == null || c == null) continue;
                        var value = key.EndsWith("Shares")
                            ? (a.Value*(latest.End.DayNumber-latest.Start.DayNumber+1)+b.Value*(ytd.End.DayNumber-ytd.Start.DayNumber+1)-c.Value*(prior.End.DayNumber-prior.Start.DayNumber+1))/(ytd.End.DayNumber-prior.End.DayNumber)
                            : a.Value+b.Value-c.Value;
                        result[key] = b with { Value = value, Start = prior.End.AddDays(1), Kind = "Derived", Derivation = $"TTM FY+YTD-priorYTD; {ValuationJson.Serialize(new[] { a,b,c })}" };
                    }
                    // Publish a derived TTM only when it has at least the same
                    // coverage as the latest FY. Otherwise the engine would
                    // silently switch from a complete annual period to a
                    // partial TTM and report avoidable "missing" valuations.
                    // Keeping the complete FY is preferable to mixing periods;
                    // the engine records that the valuation is FY based.
                    var lostCoverage = latest.Facts.Keys.Except(result.Keys).ToArray();
                    if (lostCoverage.Length == 0)
                        latest = new(prior.End.AddDays(1), ytd.End, true, result);
                    else
                        warnings.Add($"TTM incomplete; latest complete FY retained (missing: {string.Join(", ", lostCoverage)}).");
                }
                else warnings.Add("Comparable prior YTD unavailable; latest FY retained.");
            }
        }
        var shares = facts.TryGetProperty("dei", out var dei) ? Read(dei, "shares", ["EntityCommonStockSharesOutstanding"])
            .Where(f => f.Start == f.End && f.Value > 0 && today.DayNumber-f.End.DayNumber <= 180).OrderByDescending(f => f.End).FirstOrDefault() : null;
        return new("USD", today, annual, latest, shares, warnings);
    }
}
