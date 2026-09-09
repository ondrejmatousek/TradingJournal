namespace TradingJournal.Services.Screening;

public sealed record ScreeningSnapshot(DateTimeOffset CalculatedAt, List<ScreeningRow> Rows, List<MacroObservation> Macro)
{
    public const string Key = "screening-results-v1";
    public static object Page(ScreeningSnapshot snapshot, int page, int pageSize, string? search, string? sector, string? filter, string? sort)
    {
        var rows = snapshot.Rows.Where(r => (string.IsNullOrWhiteSpace(search) || $"{r.Symbol} {r.Name}".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(sector) || r.Sector == sector));
        rows = rows.Where(r => filter switch
        {
            "rated" => r.Score != null, "discount" => r.Discount > 0,
            "ema" => r.EmaDistance < 0, "season" => r.Seasonality.FirstOrDefault(s => s.Days == 63)?.Median > 0,
            _ => true
        });
        decimal? Metric(ScreeningRow r) => sort switch
        {
            "score" => r.Score, "discount" => r.Discount,
            "quality" => r.Pillars.FirstOrDefault(p => p.Key == "quality")?.Score,
            "season" => r.Seasonality.FirstOrDefault(s => s.Days == 63)?.Median, _ => r.Coverage
        };
        rows = sort == "symbol" ? rows.OrderBy(r => r.Symbol) : rows.OrderByDescending(Metric).ThenBy(r => r.Symbol);
        var matches = rows.ToArray();
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(matches.Length / (double)pageSize)));
        return new { rows = matches.Skip((page - 1) * pageSize).Take(pageSize), total = matches.Length,
            universeTotal = snapshot.Rows.Count, rated = snapshot.Rows.Count(r => r.Score != null),
            discounted = snapshot.Rows.Count(r => r.Discount > 0), page, pageSize,
            sectors = InvestmentUniverse.All.Select(s => s.Sector).Distinct().Order().ToArray(),
            macro = snapshot.Macro, calculatedAt = snapshot.CalculatedAt };
    }
}
