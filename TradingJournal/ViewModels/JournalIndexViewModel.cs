using Microsoft.AspNetCore.Mvc.Rendering;
using TradingJournal.Models;

namespace TradingJournal.ViewModels
{
    public class JournalIndexViewModel
    {
        public IReadOnlyList<JournalEntry> Entries { get; init; } = [];

        public IReadOnlyList<JournalEntry> SelectedDayEntries { get; init; } = [];

        public JournalIndexFilters Filters { get; init; } = new();

        public JournalStats Stats { get; init; } = new();

        public JournalCalendar Calendar { get; init; } = new();

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

        public DateOnly? Month { get; init; }

        public DateOnly? SelectedDate { get; init; }
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

    public class JournalCalendar
    {
        public DateOnly Month { get; init; }

        public DateOnly PreviousMonth { get; init; }

        public DateOnly NextMonth { get; init; }

        public DateOnly SelectedDate { get; init; }

        public IReadOnlyList<JournalCalendarDay> Days { get; init; } = [];
    }

    public class JournalCalendarDay
    {
        public DateOnly Date { get; init; }

        public bool IsCurrentMonth { get; init; }

        public bool IsSelected { get; init; }

        public int TradeCount { get; init; }

        public decimal NetMove { get; init; }

        public string Tone =>
            TradeCount == 0 ? "empty" :
            NetMove > 0 ? "win" :
            NetMove < 0 ? "loss" :
            "flat";
    }
}
