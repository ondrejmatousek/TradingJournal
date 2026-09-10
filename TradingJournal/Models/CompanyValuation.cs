namespace TradingJournal.Models;

/// <summary>Immutable daily valuation result. Method details remain JSON so new methods do not require a schema rewrite.</summary>
public sealed class CompanyValuation
{
    public long Id { get; set; }
    public string Symbol { get; set; } = "";
    public DateOnly ValuationDate { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal FairValueLow { get; set; }
    public decimal FairValueBase { get; set; }
    public decimal FairValueHigh { get; set; }
    public decimal? UpsidePercent { get; set; }
    public decimal? MarginOfSafetyPercent { get; set; }
    public decimal? ValuationScore { get; set; }
    public decimal Confidence { get; set; }
    public string Status { get; set; } = "N/A";
    public string Model { get; set; } = "";
    public string MethodsJson { get; set; } = "{}";
    public string AssumptionsJson { get; set; } = "{}";
    public string DataQuality { get; set; } = "";
}
