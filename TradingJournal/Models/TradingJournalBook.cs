using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace TradingJournal.Models;

public class TradingJournalBook
{
    public int Id { get; set; }
    [BindNever, ValidateNever]
    public string UserId { get; set; } = null!;
    [Required(ErrorMessage = "Zadejte název deníku."), StringLength(120)]
    public string Name { get; set; } = "";
    [StringLength(1000)]
    public string? Description { get; set; }
    [BindNever, ValidateNever]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [BindNever, ValidateNever]
    public ICollection<JournalEntry> Entries { get; set; } = new List<JournalEntry>();
}
