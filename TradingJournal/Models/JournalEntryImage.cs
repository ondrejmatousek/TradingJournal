using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models
{
    public class JournalEntryImage
    {
        public int Id { get; set; }

        public int JournalEntryId { get; set; }

        [BindNever]
        [ValidateNever]
        public JournalEntry JournalEntry { get; set; } = null!;

        public byte[] ImageData { get; set; } = [];

        [MaxLength(100)]
        public string ImageContentType { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? ImageFileName { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
