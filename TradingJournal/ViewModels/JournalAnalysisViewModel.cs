using TradingJournal.Models;

namespace TradingJournal.ViewModels
{
    public class JournalAnalysisViewModel
    {
        public AnalysisStats Stats { get; init; } = new();

        public IReadOnlyList<AnalysisPoint> EquityCurve { get; init; } = [];

        public IReadOnlyList<AnalysisBar> MonthlyPerformance { get; init; } = [];

        public IReadOnlyList<AnalysisBar> DailyPerformance { get; init; } = [];

        public IReadOnlyList<AssetBreakdown> AssetBreakdown { get; init; } = [];

        public IReadOnlyList<SideBreakdown> SideBreakdown { get; init; } = [];

        public IReadOnlyList<RecentTradeRow> RecentTrades { get; init; } = [];
    }

    public class AnalysisStats
    {
        public int TotalTrades { get; init; }

        public int ClosedTrades { get; init; }

        public int OpenTrades { get; init; }

        public int Wins { get; init; }

        public int Losses { get; init; }

        public int Breakeven { get; init; }

        public decimal WinRate { get; init; }

        public decimal PlanAdherence { get; init; }

        public decimal AverageRisk { get; init; }

        public decimal NetPriceMove { get; init; }

        public decimal AveragePriceMove { get; init; }
    }

    public class AnalysisPoint
    {
        public string Label { get; init; } = string.Empty;

        public decimal Value { get; init; }
    }

    public class AnalysisBar
    {
        public string Label { get; init; } = string.Empty;

        public decimal Value { get; init; }
    }

    public class AssetBreakdown
    {
        public AssetSymbol Asset { get; init; }

        public int Trades { get; init; }

        public decimal WinRate { get; init; }

        public decimal NetPriceMove { get; init; }
    }

    public class SideBreakdown
    {
        public PositionSide Side { get; init; }

        public int Trades { get; init; }

        public decimal WinRate { get; init; }

        public decimal NetPriceMove { get; init; }
    }

    public class RecentTradeRow
    {
        public int Id { get; init; }

        public string Date { get; init; } = string.Empty;

        public AssetSymbol Asset { get; init; }

        public PositionSide Side { get; init; }

        public TradeOutcome Outcome { get; init; }

        public EntryStatus Status { get; init; }

        public decimal? PriceMove { get; init; }
    }
}
