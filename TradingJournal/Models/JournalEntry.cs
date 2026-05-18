using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models
{
    public class JournalEntry
    {
        public int Id { get; set; }

        [BindNever]
        [ValidateNever]
        public string UserId { get; set; } = null!;

        [Required]
        public AssetSymbol Asset { get; set; }

        [Required]
        public PositionSide Side { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public DateTimeOffset? EntryTime { get; set; }

        public DateTimeOffset? ExitTime { get; set; }

        [MaxLength(4000)]
        public string? Note { get; set; }

        [MaxLength(2000)]
        public string? FeelingsBeforeEntry { get; set; }

        public TradeOutcome Outcome { get; set; } = TradeOutcome.Unknown;

        public decimal RiskPercentage { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal? ExitPrice { get; set; }

        public bool FollowedTradingPlan { get; set; }

        public EntryStatus Status { get; set; } = EntryStatus.Executed;

        public ICollection<JournalEntryImage> Images { get; set; } = new List<JournalEntryImage>();

        public byte[]? ImageData { get; set; }

        [MaxLength(100)]
        public string? ImageContentType { get; set; }

        [MaxLength(255)]
        public string? ImageFileName { get; set; }
    }
}
