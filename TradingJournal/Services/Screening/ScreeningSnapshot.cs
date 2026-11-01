namespace TradingJournal.Services.Screening;

public sealed record ScreeningSnapshot(DateTimeOffset CalculatedAt, List<ScreeningRow> Rows, List<MacroObservation> Macro)
{
    public static ScreeningRow Compact(ScreeningRow row)
    {
        if(row.Fundamentals is {} f) row.Fundamentals=f with {ValuationFinancials=null,HistoricalFinancialsJson="",HistoricalRevenueJson=""};
        return row;
    }
    public const string Key = "screening-results-v2";
    public static object Page(ScreeningSnapshot snapshot, int page, int pageSize, string? search, string? sector, string? filter, string? sort,
        decimal? minDiscount = null, decimal? minScore = null, decimal? minConfidence = null)
    {
        var rows = snapshot.Rows.Where(r => (string.IsNullOrWhiteSpace(search) || $"{r.Symbol} {r.Name}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(sector) || r.Sector == sector));
        rows = rows.Where(r => filter switch
        {
            "candidate" => r.Entry?.Status == "candidate", "wait" => r.Entry?.Status is "wait" or "expensive",
            "risk" => r.Entry?.Status == "risk", "missing" => r.Entry == null || r.Entry.Status == "insufficient",
            "rated" => r.Score != null, "discount" => r.Discount > 0,
            "ema" => r.EmaDistance < 0,
            _ => true
        } && (!minDiscount.HasValue || r.Discount >= minDiscount) && (!minScore.HasValue || r.ValuationScore >= minScore)
            && (!minConfidence.HasValue || r.ValuationConfidence >= minConfidence));
        decimal? Metric(ScreeningRow r) => sort switch
        {
            "score" => r.Score, "discount" => r.Discount, "confidence" => r.ValuationConfidence, "upside" => r.Discount,
            "quality" => r.Pillars.FirstOrDefault(p => p.Key == "quality")?.Score,
            _ => r.Coverage
        };
        rows = sort == "symbol" ? rows.OrderBy(r => r.Symbol) : rows.OrderByDescending(Metric).ThenBy(r => r.Symbol);
        var matches = rows.ToArray();
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(matches.Length / (double)pageSize)));
        return new { rows = matches.Skip((page - 1) * pageSize).Take(pageSize), total = matches.Length,
            universeTotal = snapshot.Rows.Count, rated = snapshot.Rows.Count(r => r.Score != null),
            discounted = snapshot.Rows.Count(r => r.Discount > 0), page, pageSize,
            sectors = snapshot.Rows.Select(s => s.Sector).Distinct().Order().ToArray(),
            macro = snapshot.Macro, calculatedAt = snapshot.CalculatedAt };
    }
}
