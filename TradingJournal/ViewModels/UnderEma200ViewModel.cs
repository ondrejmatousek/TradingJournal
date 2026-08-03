namespace TradingJournal.ViewModels
{
    public class UnderEma200Result
    {
        public DateTimeOffset? UpdatedAt { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Timeframe { get; set; } = "Weekly";
        public string TimeframeLabel { get; set; } = "Weekly EMA 200";
        public string EmaLabel { get; set; } = "Weekly EMA200";
        public IReadOnlyList<UnderEma200StockCard> Items { get; set; } = [];
        public int QualifiedCount => Items.Count(item => item.IsQualified);
        public int BelowEmaCount => Items.Count(item => item.IsBelowEma200);
        public int FailedCount => Items.Count(item => item.IsBelowEma200 && !item.IsQualified);
    }

    public class UnderEma200StockCard
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public string Timeframe { get; set; } = "Weekly";
        public decimal CurrentPrice { get; set; }
        public decimal Ema200 { get; set; }
        public decimal DistanceFromEmaPercent { get; set; }
        public decimal Performance1YearPercent { get; set; }
        public decimal Performance3YearPercent { get; set; }
        public decimal Performance5YearPercent { get; set; }
        public int Score { get; set; }
        public bool IsBelowEma200 { get; set; }
        public bool IsQualified { get; set; }
        public string FailureReasons { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime LastPriceDate { get; set; }
        public DateTime CalculationDate { get; set; }
        public DateTime SnapshotCreatedAtUtc { get; set; }
        public string Rationale { get; set; } = string.Empty;
        public string DataSource { get; set; } = string.Empty;
        public string ChartUrl { get; set; } = string.Empty;
    }
}
