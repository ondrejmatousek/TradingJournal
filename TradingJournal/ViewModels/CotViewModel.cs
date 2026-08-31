using TradingJournal.Models;

namespace TradingJournal.ViewModels;

public class CotDashboardViewModel
{
    public IReadOnlyList<CotDashboardItem> Items { get; init; } = [];

    public DateTimeOffset? LastSuccessfulImport { get; init; }

    public string? LastImportMessage { get; init; }

    public bool IsImportInProgress { get; init; }
}

public sealed class CotDashboardItem
{
    public string SymbolKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string CotSymbolKey { get; init; } = string.Empty;

    public CotReport? Latest { get; init; }

    public bool IsStale { get; init; }

    public string StaleMessage { get; init; } = string.Empty;

    public string TraderCategory { get; init; } = string.Empty;

    public IReadOnlyList<CotChartPoint> History { get; init; } = [];

    public CotNarrative Narrative { get; init; } = new();
}

public sealed class CotNarrative
{
    public string Headline { get; init; } = string.Empty;

    public string Overview { get; init; } = string.Empty;

    public IReadOnlyList<string> BullishArguments { get; init; } = [];

    public IReadOnlyList<string> WarningArguments { get; init; } = [];

    public string PracticalUse { get; init; } = string.Empty;
}

public sealed class CotChartPoint
{
    public DateOnly ReportDate { get; init; }

    public decimal NetPosition { get; init; }

    public decimal LongPosition { get; init; }

    public decimal ShortPosition { get; init; }

    public decimal? CotIndex52 { get; init; }

    public decimal? CommercialsNetPosition { get; init; }

    public decimal? RetailersNetPosition { get; init; }
}
