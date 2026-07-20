namespace TradingJournal.Models
{
    public class StrategyNote
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string StrategyKey { get; set; } = string.Empty;

        public string ContentHtml { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
