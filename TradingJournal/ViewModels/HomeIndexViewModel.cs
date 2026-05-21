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

    public class MarketRegimeCard
    {
        public string Regime { get; set; } = "Mixed";
        public int Score { get; set; }
        public string Tone { get; set; } = "neutral";
        public string Summary { get; set; } = string.Empty;
        public IReadOnlyList<string> Drivers { get; set; } = [];
    }

    public class EconomicEventCard
    {
        public string Time { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string Impact { get; set; } = "Medium";
        public string Note { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? Actual { get; set; }
        public string? Forecast { get; set; }
        public string? Previous { get; set; }
    }

    public class TradeSetupCard
    {
        public string Symbol { get; set; } = string.Empty;
        public string Bias { get; set; } = "Wait";
        public string Trigger { get; set; } = string.Empty;
        public string Invalidation { get; set; } = string.Empty;
        public string Focus { get; set; } = string.Empty;
    }
}
