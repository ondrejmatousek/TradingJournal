namespace TradingJournal.ViewModels;

public sealed class CotContextViewModel
{
    public string SymbolKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Bias { get; init; } = "Neutral";

    public DateOnly? ReportDate { get; init; }

    public long? NetPosition { get; init; }

    public long? WeeklyNetChange { get; init; }

    public decimal? Percentile52 { get; init; }

    public decimal? ZScore { get; init; }

    public bool IsStale { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string DirectionAssessment(string? direction)
    {
        var normalizedDirection = direction?.Trim().ToUpperInvariant();
        if (Bias == "Neutral" || normalizedDirection is not ("LONG" or "SHORT"))
            return "Neutrální COT kontext.";

        var aligned = (Bias == "Bullish" && normalizedDirection == "LONG")
            || (Bias == "Bearish" && normalizedDirection == "SHORT");

        return aligned
            ? "Směr tradu je v souladu s COT biasem; představuje plusovou makro konfluenci."
            : "Trade jde proti COT biasu; není to automaticky no-trade, ale je to slabina setupu.";
    }
}
