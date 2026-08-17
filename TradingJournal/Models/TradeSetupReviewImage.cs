using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models
{
    public class TradeSetupReviewImage
    {
        public int Id { get; set; }

        public int TradeSetupReviewId { get; set; }

        [BindNever]
        [ValidateNever]
        public TradeSetupReview TradeSetupReview { get; set; } = null!;

        public byte[] ImageData { get; set; } = [];

        [MaxLength(100)]
        public string ImageContentType { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? ImageFileName { get; set; }

        [MaxLength(80)]
        public string? Label { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
