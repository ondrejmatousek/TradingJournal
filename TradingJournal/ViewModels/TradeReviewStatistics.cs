using TradingJournal.Models;

namespace TradingJournal.ViewModels;

public class EarlyExitSummary
{
    public TradeOutcome Outcome { get; init; }
    public int Total { get; init; }
    public int WouldWin { get; init; }
    public int WouldLose { get; init; }
    public int WouldBreakeven { get; init; }
    public int Unknown { get; init; }
    public decimal? WouldWinRate => Total == Unknown ? null : WouldWin * 100m / (Total - Unknown);
}

public class SeasonalitySummary
{
    public bool? Aligned { get; init; }
    public int Total { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public decimal? ProfitLoss { get; init; }
    public decimal? WinRate => Wins + Losses == 0 ? null : Wins * 100m / (Wins + Losses);
}
