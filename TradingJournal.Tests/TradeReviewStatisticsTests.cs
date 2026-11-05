using TradingJournal.Models;
using TradingJournal.Services;

namespace TradingJournal.Tests;

public class TradeReviewStatisticsTests
{
    [Fact]
    public void UnknownCounterfactualsAreExcludedAndManualMoneyRetainsItsExitReason()
    {
        JournalEntry[] trades = [
            new() { Outcome = TradeOutcome.Manual, ProfitLoss = -50, PlannedOutcome = PlannedTradeOutcome.Win, SeasonalityAligned = true },
            new() { Outcome = TradeOutcome.Manual, ProfitLoss = 100, PlannedOutcome = PlannedTradeOutcome.Loss, SeasonalityAligned = false },
            new() { Outcome = TradeOutcome.Manual },
            new() { Outcome = TradeOutcome.Breakeven, ProfitLoss = 0, PlannedOutcome = PlannedTradeOutcome.Breakeven },
            new() { Status = EntryStatus.Open, Outcome = TradeOutcome.Manual, ProfitLoss = 999, PlannedOutcome = PlannedTradeOutcome.Win },
            new() { Status = EntryStatus.Skipped, Outcome = TradeOutcome.Manual, ProfitLoss = 999, PlannedOutcome = PlannedTradeOutcome.Win }
        ];
        var manual = TradeReviewStatistics.EarlyExits(trades).Single(x => x.Outcome == TradeOutcome.Manual);
        Assert.Equal(3, manual.Total);
        Assert.Equal(1, manual.Unknown);
        Assert.Equal(50m, manual.WouldWinRate);
        var seasonality = TradeReviewStatistics.Seasonality(trades);
        Assert.Equal(-50m, seasonality.Single(x => x.Aligned == true).ProfitLoss);
        Assert.Equal(100m, seasonality.Single(x => x.Aligned == false).WinRate);
        Assert.Equal(0m, seasonality.Single(x => x.Aligned == null).ProfitLoss);
        Assert.Null(TradeReviewStatistics.Seasonality([new JournalEntry()]).Single(x => x.Aligned == null).ProfitLoss);
        Assert.Null(TradeReviewStatistics.EarlyExits([new JournalEntry { Outcome = TradeOutcome.Manual }]).Single(x => x.Outcome == TradeOutcome.Manual).WouldWinRate);
    }
}
