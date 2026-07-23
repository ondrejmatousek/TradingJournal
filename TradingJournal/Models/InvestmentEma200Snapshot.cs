namespace TradingJournal.Models
{
    public class InvestmentEma200Snapshot
    {
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal Ema200Daily { get; set; }
        public decimal Ema200Weekly { get; set; }
        public decimal Performance1YearPercent { get; set; }
        public decimal Performance3YearPercent { get; set; }
        public decimal Performance5YearPercent { get; set; }
        public int DailyPriceCount { get; set; }
        public int DailyEmaSourceCount { get; set; }
        public int WeeklyEmaSourceCount { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime LastPriceDate { get; set; }
        public DateTime CalculationDate { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTime SnapshotCreatedAtUtc { get; set; }
        public string DataSource { get; set; } = string.Empty;
    }
}
