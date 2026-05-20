namespace TradingJournal.ViewModels
{
    public class HomeIndexViewModel
    {
        public IReadOnlyList<MarketBiasCard> MarketBiases { get; set; } = [];
        public DateTimeOffset? MarketBiasUpdatedAt { get; set; }
        public bool MarketBiasIsLive { get; set; }
        public IReadOnlyList<StockIdeaCard> StockIdeas { get; set; } = [];
        public DateTimeOffset? StockRadarUpdatedAt { get; set; }
        public bool StockRadarConfigured { get; set; }
        public string StockRadarMessage { get; set; } = string.Empty;
    }

    public class MarketBiasCard
    {
        public string Symbol { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Direction { get; set; } = "Neutral";
        public int SignalScore { get; set; }
        public int ConfidenceStars { get; set; }
        public decimal? LastClose { get; set; }
        public decimal? ChangePercent { get; set; }
        public decimal? Rsi14 { get; set; }
        public string Summary { get; set; } = string.Empty;
        public IReadOnlyList<string> Bullets { get; set; } = [];
        public string Source { get; set; } = string.Empty;
    }

    public class StockIdeaCard
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public int Score { get; set; }
        public decimal? Price { get; set; }
        public decimal? TargetPrice { get; set; }
        public decimal? TargetUpsidePercent { get; set; }
        public decimal? AllTimeHighPrice { get; set; }
        public decimal? DiscountFromAthPercent { get; set; }
        public decimal? PeRatio { get; set; }
        public decimal? PegRatio { get; set; }
        public decimal? RevenueGrowthPercent { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = [];
        public string Source { get; set; } = string.Empty;
    }
}
