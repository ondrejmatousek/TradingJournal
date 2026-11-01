using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Services.Valuation;

public sealed class PriceLabSettings
{
    [Range(4, 30)] public decimal RequiredReturnPercent { get; set; } = 10;
    [Range(-10, 50)] public decimal? GrowthPercent { get; set; }
    [Range(1, 60)] public decimal? MarginPercent { get; set; }
    [Range(.3, 3)] public decimal? Beta { get; set; }
    [Range(.01, 100)] public decimal? ReferencePe { get; set; }
    [Range(.0001, 1000000)] public decimal? ForwardEps { get; set; }
    [StringLength(2000)] public string SourceUrl { get; set; } = "";
    public DateOnly? SourceDate { get; set; }

    public bool HasOverrides => GrowthPercent != null || MarginPercent != null || Beta != null || ReferencePe != null || ForwardEps != null;
    public string? Validate(DateOnly today)
    {
        if (!Validator.TryValidateObject(this, new ValidationContext(this), [], true))
            return "Výnos musí být 4–30 %, počáteční růst −10 až 50 %, cílová EBIT marže 1–60 %, beta 0,3–3 a P/E 0,01–100.";
        if ((ReferencePe == null) != (ForwardEps == null)) return "Pro srovnávací ocenění vyplň současně budoucí EPS i referenční P/E.";
        if ((HasOverrides || !string.IsNullOrWhiteSpace(SourceUrl) || SourceDate != null) && (!Uri.TryCreate(SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo)
            || SourceDate == null || SourceDate > today || SourceDate < today.AddYears(-2)))
            return "Vlastní předpoklady potřebují HTTPS zdroj a datum publikace z posledních dvou let, nejvýše dnešní.";
        return null;
    }

    public ValuationForecast? Forecast => GrowthPercent != null || MarginPercent != null || Beta != null
        ? new(GrowthPercent / 100, MarginPercent / 100, Beta, SourceUrl, SourceDate ?? default) : null;
}

public sealed record ValuationForecast(decimal? InitialGrowth, decimal? TerminalMargin, decimal? Beta, string Source, DateOnly Date);
public sealed record ReverseDcfResult(string Status, decimal? InitialGrowthPercent, decimal? RevenueYear10, string Explanation);
public sealed record PriceLabResult(string Symbol, string Name, decimal? Price, DateOnly? PriceDate, DateOnly? FinancialPeriod,
    PriceLabSettings Settings, ValuationReport Report, decimal? ReturnPrice, decimal? ConservativeLimit,
    decimal? MultipleValue, bool ReviewRequired, ReverseDcfResult ReverseDcf, List<string> Notes)
{
    public List<string> FinancialSources { get; init; } = [];
    public DateOnly? LatestFiled { get; init; }
}
