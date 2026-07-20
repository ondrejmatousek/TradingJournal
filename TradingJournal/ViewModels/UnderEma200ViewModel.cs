namespace TradingJournal.ViewModels
{
    public class UnderEma200Result
    {
        public DateTimeOffset? UpdatedAt { get; set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<UnderEma200StockCard> Items { get; set; } = [];
    }

    public class UnderEma200StockCard
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal WeeklyEma200 { get; set; }
        public decimal DiscountToEmaPercent { get; set; }
        public decimal EmaSlope52WeekPercent { get; set; }
        public decimal Return5YearPercent { get; set; }
        public decimal WeeksAboveEmaSharePercent { get; set; }
        public int Score { get; set; }
        public DateTime LastPriceDate { get; set; }
        public string Rationale { get; set; } = string.Empty;
        public string DataSource { get; set; } = string.Empty;
        public string ChartUrl { get; set; } = string.Empty;
    }
}
