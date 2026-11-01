using TradingJournal.Models;

namespace TradingJournal.Services.Screening;

public sealed record InvestmentSymbol(string Symbol, string Name, string Sector);

public static class InvestmentUniverse
{
    public static IReadOnlyList<InvestmentSymbol> All { get; } = Build();
    private static IReadOnlyList<InvestmentSymbol> Build()
    {
        var sectors = new Dictionary<string, string>
        {
            ["Technology"] = "MSFT AAPL ORCL CRM ADBE INTU NOW IBM ACN CSCO PANW CRWD FTNT SNPS CDNS ADSK ANET DELL HPQ",
            ["Semiconductors"] = "NVDA AVGO AMD INTC QCOM TXN ADI MU AMAT LRCX KLAC MCHP NXPI ON MPWR TSM ASML",
            ["Communication Services"] = "GOOGL META NFLX DIS CMCSA TMUS VZ T EA TTWO RBLX",
            ["Consumer Cyclical"] = "AMZN TSLA HD LOW NKE SBUX MCD BKNG ABNB MAR HLT CMG YUM GM F APTV ORLY AZO DHI LEN ROST TJX",
            ["Consumer Defensive"] = "WMT COST PG KO PEP MDLZ CL KMB GIS HSY KR KHC STZ PM MO",
            ["Healthcare"] = "UNH JNJ LLY ABBV MRK PFE AMGN GILD BMY REGN VRTX ISRG TMO DHR ABT MDT SYK BSX CVS CI HUM ZTS",
            ["Financial Services"] = "JPM BAC WFC C GS MS BLK SCHW AXP V MA SPGI MCO CME ICE CB PGR TRV AFL AIG MET PRU",
            ["Industrials"] = "CAT DE HON GE RTX LMT NOC GD BA ETN EMR ITW PH ROK UPS FDX UNP CSX NSC WM RSG CTAS URI PWR PCAR",
            ["Energy"] = "XOM CVX COP EOG OXY SLB HAL MPC VLO PSX KMI WMB OKE",
            ["Basic Materials"] = "LIN APD SHW ECL DOW DD NUE STLD FCX NEM VMC MLM",
            ["Utilities"] = "NEE DUK SO AEP SRE EXC XEL ED D PEG",
            ["Real Estate"] = "PLD AMT EQIX O WELL SPG DLR PSA CCI VICI AVB EQR"
        };
        var names = InvestmentAssetCatalog.Assets.GroupBy(a => a.YahooSymbol).ToDictionary(g => g.Key, g => g.First().Name);
        return sectors.SelectMany(s => s.Value.Split(' ').Select(t => new InvestmentSymbol(t, names.GetValueOrDefault(t, t), s.Key))).OrderBy(s => s.Symbol).ToArray();
    }
}

public sealed record ScreeningPillar(string Key, string Label, int Weight, decimal? Score, string Explanation);
public sealed record SeasonalWindow(int Days, int Years, decimal? Median, decimal? WinRate, decimal? Worst, decimal? RecentMedian);
public sealed record FundamentalInput(DateOnly PeriodEnd, DateOnly Filed, decimal Revenue, decimal PreviousRevenue,
    decimal OperatingCashFlow, decimal CapitalExpenditure, decimal NetIncome, decimal Assets, decimal Liabilities,
    decimal Shares, string Source, string Currency = "USD")
{
    public TradingJournal.Services.Valuation.FinancialDataset? ValuationFinancials { get; init; }
    public decimal? CashAndEquivalents { get; init; }
    public decimal? TotalDebt { get; init; }
    // Compact JSON keeps historical annual observations in the existing cache without a second provider or table.
    public string HistoricalRevenueJson { get; init; } = "";
    public string HistoricalFinancialsJson { get; init; } = "";
    public decimal? Ebitda { get; init; }
    public decimal? ForwardEps { get; init; }
}
public sealed record ValuationResult(decimal Low, decimal Base, decimal High, decimal Growth, decimal DiscountRate, decimal TerminalGrowth)
{
    public string? RunId { get; init; }
    public string? Uncertainty { get; init; }
    public string Model { get; init; } = "Legacy DCF";
    public decimal MarginOfSafety { get; init; }
    public decimal BuyBelow { get; init; }
    public decimal? DcfFairValue { get; init; }
    public decimal? RelativeFairValue { get; init; }
    public decimal? FcfFairValue { get; init; }
    public decimal? EvEbitdaFairValue { get; init; }
    public decimal? ForwardPeFairValue { get; init; }
    public decimal? RelativeMultiple { get; init; }
    public decimal? CompositeFairValue { get; init; }
    public decimal? ValuationScore { get; init; }
    public decimal Confidence { get; init; }
    public string Status { get; init; } = "N/A";
    public decimal RiskFreeRate { get; init; }
    public decimal EquityRiskPremium { get; init; } = 0.05m;
    public decimal Beta { get; init; } = 1m;
    public decimal CostOfDebt { get; init; }
    public decimal TaxRate { get; init; }
    public bool WaccFallbackUsed { get; init; }
    public decimal? Cash { get; init; }
    public decimal? Debt { get; init; }
    public decimal? DilutedShares { get; init; }
    public string DataQuality { get; init; } = "";
    // JSON strings keep record equality deterministic for cache/tests while remaining inspectable in the detail view.
    public string Weights { get; init; } = "";
    public string Explanation { get; init; } = "";
}
public sealed record EntryAssessment(string Status, string Label, decimal? ZoneLow, decimal? ZoneHigh,
    decimal? Support, decimal? Momentum, List<string> Reasons);
public sealed record MacroObservation(string Series, DateOnly Date, decimal Value, decimal? PriorValue, string Source);
public sealed record ResearchEvidence(string Pillar, decimal Score, DateOnly Published, string SourceUrl,
    string Author, string Thesis, string Risk, int HorizonDays, bool TrackRecordVerified = false);
public sealed class ScreeningResearch
{
    public List<ResearchEvidence> Evidence { get; set; } = [];
    public FundamentalInput? Fundamentals { get; set; }
}
public sealed class ScreeningRow
{
    public CoverageAudit? DataAudit { get; set; }
    public string Decision => ScreeningCoverage.Decision(this);
    public string? ValuationRunId { get; set; }
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sector { get; set; } = "";
    public decimal? Price { get; set; }
    public DateOnly? PriceDate { get; set; }
    public decimal? EmaDistance { get; set; }
    public decimal? Score { get; set; }
    public int Coverage { get; set; }
    public string Verdict { get; set; } = "Nedostatek dat";
    public List<ScreeningPillar> Pillars { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<SeasonalWindow> Seasonality { get; set; } = [];
    public FundamentalInput? Fundamentals { get; set; }
    public ValuationResult? Valuation { get; set; }
    public decimal? Discount { get; set; }
    public decimal? ValuationScore { get; set; }
    public decimal? ValuationConfidence { get; set; }
    public string ValuationStatus { get; set; } = "N/A";
    public EntryAssessment? Entry { get; set; }
    public List<ResearchEvidence> Evidence { get; set; } = [];
    public string? RefreshMessage { get; set; }
}

