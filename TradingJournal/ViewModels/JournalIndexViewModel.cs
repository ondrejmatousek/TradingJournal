using Microsoft.AspNetCore.Mvc.Rendering;
using TradingJournal.Models;

namespace TradingJournal.ViewModels
{
    public class JournalIndexViewModel
    {
        public IReadOnlyList<JournalEntry> Entries { get; init; } = [];

        public JournalIndexFilters Filters { get; init; } = new();

        public JournalStats Stats { get; init; } = new();

        public IReadOnlyList<SelectListItem> AssetOptions { get; init; } = [];

        public IReadOnlyList<SelectListItem> OutcomeOptions { get; init; } = [];

        public IReadOnlyList<SelectListItem> SideOptions { get; init; } = [];
    }

    public class JournalIndexFilters
    {
        public AssetSymbol? Asset { get; init; }

        public TradeOutcome? Outcome { get; init; }

        public PositionSide? Side { get; init; }

        public bool PlanOnly { get; init; }
    }

    public class JournalStats
    {
        public int TotalTrades { get; init; }

        public int Wins { get; init; }

        public int Losses { get; init; }

        public int Breakeven { get; init; }

        public int Unknown { get; init; }

        public decimal WinRate { get; init; }

        public decimal PlanAdherence { get; init; }

        public decimal AverageRisk { get; init; }

        public AssetSymbol? BestAsset { get; init; }

        public decimal? BestAssetWinRate { get; init; }
    }
}
