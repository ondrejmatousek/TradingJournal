namespace TradingJournal.Models;

// Shared durable cache, personal research and job state. Hash preserves legacy file identifiers.
public sealed class ScreeningDocument
{
    public string Id { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
