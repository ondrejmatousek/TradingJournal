using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Services;

public static class TradeReviewStatistics
{
    public static IReadOnlyList<EarlyExitSummary> EarlyExits(IEnumerable<JournalEntry> entries) =>
        new[] { TradeOutcome.Breakeven, TradeOutcome.Manual }.Select(outcome =>
        {
            var trades = entries.Where(e => e.Status == EntryStatus.Executed && e.Outcome == outcome).ToList();
            return new EarlyExitSummary
            {
                Outcome = outcome,
                Total = trades.Count,
                WouldWin = trades.Count(e => e.PlannedOutcome == PlannedTradeOutcome.Win),
                WouldLose = trades.Count(e => e.PlannedOutcome == PlannedTradeOutcome.Loss),
                WouldBreakeven = trades.Count(e => e.PlannedOutcome == PlannedTradeOutcome.Breakeven),
                Unknown = trades.Count(e => !e.PlannedOutcome.HasValue)
            };
        }).ToList();

    public static IReadOnlyList<SeasonalitySummary> Seasonality(IEnumerable<JournalEntry> entries) =>
        new bool?[] { true, false, null }.Select(aligned =>
        {
            var trades = entries.Where(e => e.Status == EntryStatus.Executed && e.SeasonalityAligned == aligned).ToList();
            // Manual exits retain their exit reason; the amount determines their financial result.
            var wins = trades.Count(IsWin);
            var losses = trades.Count(IsLoss);
            return new SeasonalitySummary
            {
                Aligned = aligned,
                Total = trades.Count,
                Wins = wins,
                Losses = losses,
                ProfitLoss = trades.Any(e => e.ProfitLoss.HasValue) ? trades.Sum(e => e.ProfitLoss ?? 0) : null
            };
        }).ToList();

    public static bool IsWin(JournalEntry entry) => entry.Status == EntryStatus.Executed &&
        (entry.Outcome == TradeOutcome.Win || entry.Outcome == TradeOutcome.Manual && entry.ProfitLoss > 0);

    public static bool IsLoss(JournalEntry entry) => entry.Status == EntryStatus.Executed &&
        (entry.Outcome == TradeOutcome.Loss || entry.Outcome == TradeOutcome.Manual && entry.ProfitLoss < 0);
}
