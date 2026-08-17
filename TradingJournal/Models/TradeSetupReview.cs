using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models
{
    public class TradeSetupReview
    {
        public int Id { get; set; }

        [BindNever]
        [ValidateNever]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string StrategyKey { get; set; } = "mucka";

        public DateOnly TradeDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [MaxLength(40)]
        public string? TradeTimeText { get; set; }

        [MaxLength(120)]
        public string Market { get; set; } = string.Empty;

        [MaxLength(16)]
        public string Direction { get; set; } = "SHORT";

        [MaxLength(120)]
        public string? Session { get; set; }

        [MaxLength(40)]
        public string Status { get; set; } = "Nevyhodnoceno";

        [MaxLength(80)]
        public string Verdict { get; set; } = "Nevyhodnoceno";

        [MaxLength(20)]
        public string? SetupRating { get; set; }

        public decimal? ProcessScore { get; set; }

        [MaxLength(120)]
        public string? Outcome { get; set; }

        public decimal? RMultiple { get; set; }

        public bool? IsValid { get; set; }

        public decimal? Entry { get; set; }

        public decimal? StopLoss { get; set; }

        public decimal? TakeProfit { get; set; }

        public decimal? PlannedRr { get; set; }

        public decimal? HtfContextScore { get; set; }

        public decimal? ZoneScore { get; set; }

        public decimal? LocalStructureScore { get; set; }

        public decimal? EntryConfirmationScore { get; set; }

        public decimal? TimingScore { get; set; }

        public decimal? RiskRewardScore { get; set; }

        public decimal? SessionScore { get; set; }

        public decimal? ManagementScore { get; set; }

        public string MissingInfo { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public string Aligned { get; set; } = string.Empty;

        public string Issues { get; set; } = string.Empty;

        public string NoTradeBlockers { get; set; } = string.Empty;

        public string? AiReviewJson { get; set; }

        public DateTimeOffset? ReviewedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public ICollection<TradeSetupReviewImage> Images { get; set; } = new List<TradeSetupReviewImage>();
    }
}
