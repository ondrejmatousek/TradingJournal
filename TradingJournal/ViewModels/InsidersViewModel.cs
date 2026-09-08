namespace TradingJournal.ViewModels;

public sealed class InsidersViewModel
{
    public string? Search { get; set; }
    public string? Sector { get; set; }
    public string? Role { get; set; }
    public decimal? MinimumValue { get; set; }
    public bool ClustersOnly { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<string> Sectors { get; set; } = [];
    public IReadOnlyList<InsiderRowViewModel> Rows { get; set; } = [];
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public int IssuersFailed { get; set; }
    public int Purchases30Days { get; set; }
    public decimal Value30Days { get; set; }
    public int ClusterSignals30Days { get; set; }
}

public sealed class InsiderRowViewModel
{
    public DateOnly TransactionDate { get; set; }
    public DateOnly FiledDate { get; set; }
    public int FilingDelayDays { get; set; }
    public string Symbol { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Sector { get; set; } = "";
    public string InsiderName { get; set; } = "";
    public string Role { get; set; } = "";
    public decimal Shares { get; set; }
    public decimal PricePerShare { get; set; }
    public decimal Value { get; set; }
    public decimal? OwnershipIncreasePercent { get; set; }
    public int ClusterInsiders { get; set; }
    public int RepeatPurchaseDays { get; set; }
    public decimal? Score { get; set; }
    public int ScoreCoverage { get; set; }
    public string ScoreExplanation { get; set; } = "";
    public bool IsAmendment { get; set; }
    public bool? IsTenB5One { get; set; }
    public string SourceUrl { get; set; } = "";
}
