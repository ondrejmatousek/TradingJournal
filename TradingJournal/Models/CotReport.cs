using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models;

public class CotReport
{
    public int Id { get; set; }

    [MaxLength(24)]
    public string SymbolKey { get; set; } = string.Empty;

    [MaxLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(180)]
    public string CftcMarketName { get; set; } = string.Empty;

    [MaxLength(24)]
    public string CftcContractMarketCode { get; set; } = string.Empty;

    [MaxLength(48)]
    public string SourceDataset { get; set; } = string.Empty;

    [MaxLength(48)]
    public string TraderCategory { get; set; } = string.Empty;

    public DateOnly ReportDate { get; set; }

    public long LongPosition { get; set; }

    public long ShortPosition { get; set; }

    public long NetPosition { get; set; }

    public long? WeeklyNetChange { get; set; }

    public long? LongChange { get; set; }

    public long? ShortChange { get; set; }

    public long OpenInterest { get; set; }

    public decimal? CotIndex26 { get; set; }

    public decimal? CotIndex52 { get; set; }

    public decimal? Percentile52 { get; set; }

    public decimal? ZScore { get; set; }

    public bool ExtremeLong { get; set; }

    public bool ExtremeShort { get; set; }

    [MaxLength(16)]
    public string Bias { get; set; } = "Neutral";

    [MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}
