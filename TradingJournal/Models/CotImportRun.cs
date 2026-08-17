using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models;

public class CotImportRun
{
    public int Id { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public bool Success { get; set; }

    public int RowsImported { get; set; }

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;
}
