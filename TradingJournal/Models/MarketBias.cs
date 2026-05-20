using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models
{
    public class MarketBias
    {
        public int Id { get; set; }

        [MaxLength(16)]
        public string Symbol { get; set; } = string.Empty;

        [MaxLength(80)]
        public string DisplayName { get; set; } = string.Empty;

        public DateTime BiasDate { get; set; }

        [MaxLength(16)]
        public string Direction { get; set; } = "Neutral";

        public int SignalScore { get; set; }

        public int ConfidenceStars { get; set; }

        public decimal? LastClose { get; set; }

        public decimal? ChangePercent { get; set; }

        public decimal? Rsi14 { get; set; }

        public decimal? Ma20 { get; set; }

        public decimal? Ma50 { get; set; }

        [MaxLength(700)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(1600)]
        public string BulletJson { get; set; } = "[]";

        [MaxLength(80)]
        public string Source { get; set; } = "Stooq";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
