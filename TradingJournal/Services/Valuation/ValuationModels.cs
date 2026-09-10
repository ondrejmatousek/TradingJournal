using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TradingJournal.Services.Valuation;

public sealed record FinancialFact(decimal Value, string Unit, DateOnly Start, DateOnly End, DateOnly Filed,
    string Source, string Tag, string Accession, string Kind = "Actual", string Derivation = "");
public sealed record FinancialPeriod(DateOnly Start, DateOnly End, bool IsTtm, Dictionary<string, FinancialFact> Facts);
public sealed record FinancialDataset(string Currency, DateOnly AsOf, List<FinancialPeriod> History,
    FinancialPeriod? Latest, FinancialFact? ActualShares, List<string> Warnings);
public sealed record Assumption(string Name, decimal Value, string Unit, string Source, DateOnly Date,
    bool IsEstimated, string Explanation);
public sealed record Normalization(string Metric, decimal Original, decimal Normalized, string Reason, string Rule = "V2.0");
public sealed record ForecastYear(int Year, decimal RevenueGrowth, decimal Revenue, decimal EbitMargin,
    decimal Ebit, decimal TaxRate, decimal Nopat, decimal Da, decimal CapEx, decimal ChangeNwc,
    decimal Fcff, decimal DiscountFactor, decimal PresentValueFcff);
public sealed record ValuationScenario(string Name, decimal Wacc, decimal TerminalGrowth, decimal TerminalRoic,
    List<ForecastYear> Years, decimal TerminalNopat, decimal TerminalReinvestment, decimal TerminalFcff,
    decimal TerminalValue, decimal PvTerminal, decimal PvExplicit, decimal EnterpriseValue, decimal Debt,
    decimal ExcessCash, decimal Investments, decimal OtherClaims, decimal EquityValue, decimal Shares, decimal FairValue);
public sealed record SensitivityCell(decimal Wacc, decimal Growth, decimal? FairValue);
public sealed record CrossCheck(string Method, decimal? CurrentMultiple, decimal? ReferenceMultiple,
    decimal? ImpliedValue, string Source, string Explanation);
public sealed record HistoricalMetric(DateOnly End, decimal Revenue, decimal? RevenueGrowth, decimal? Ebit,
    decimal? EbitMargin, decimal? Da, decimal? CapEx, decimal? ChangeNwc, decimal? Fcff, decimal? DilutedShares,
    decimal? CapExRevenue, decimal? DaRevenue, decimal? NwcRevenue);
public sealed class ValuationReport
{
    public string ModelVersion { get; set; } = "ValuationV2.0";
    public string Id { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Strategy { get; set; } = "StandardNonFinancial";
    public string Currency { get; set; } = "USD";
    public ValuationOptions Policy { get; set; } = new();
    public DateOnly AsOf { get; set; }
    public decimal? Price { get; set; }
    public DateOnly? PriceDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "LimitedData";
    public string Uncertainty { get; set; } = "VeryHigh";
    public int DataQuality { get; set; }
    public decimal MarginOfSafety { get; set; }
    public decimal? BuyBelow { get; set; }
    public string ShareType { get; set; } = "";
    public DateOnly? SharesDate { get; set; }
    public bool IsTtm { get; set; }
    public DateOnly? FinancialPeriodEnd { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<Assumption> Assumptions { get; set; } = [];
    public List<Normalization> Normalizations { get; set; } = [];
    public List<HistoricalMetric> History { get; set; } = [];
    public List<ValuationScenario> Scenarios { get; set; } = [];
    public List<SensitivityCell> Sensitivity { get; set; } = [];
    public List<CrossCheck> CrossChecks { get; set; } = [];
    public decimal? BaseFairValue => Scenarios.FirstOrDefault(s => s.Name == "Base")?.FairValue;
}
public sealed class ValuationOptions
{
    public bool Enabled { get; set; } = true;
    public decimal Erp { get; set; } = .05m;
    public decimal FallbackRiskFree { get; set; } = .045m;
    public decimal FallbackBeta { get; set; } = 1m;
    public decimal CreditSpread { get; set; } = .02m;
    public decimal FallbackTax { get; set; } = .25m;
    public decimal TerminalGrowth { get; set; } = .025m;
    public decimal TerminalRoic { get; set; } = .12m;
    public decimal MaximumForecastRoic { get; set; } = .60m;
    public decimal OperatingCashRevenue { get; set; } = .02m;
    public decimal GrowthFloor { get; set; } = -.10m;
    public decimal GrowthCeiling { get; set; } = .20m;
    public decimal TaxFloor { get; set; } = .05m;
    public decimal TaxCeiling { get; set; } = .40m;
    public decimal ScenarioGrowthShift { get; set; } = .02m;
    public decimal ScenarioMarginShift { get; set; } = .02m;
    public decimal ScenarioWaccShift { get; set; } = .005m;
    public decimal ScenarioTerminalShift { get; set; } = .005m;
    public decimal LowMos { get; set; } = .10m;
    public decimal MediumMos { get; set; } = .15m;
    public decimal HighMos { get; set; } = .25m;
    public decimal VeryHighMos { get; set; } = .35m;
}
public static class ValuationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
}
