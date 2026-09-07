using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public sealed class ScreeningScheduleTests
{
    [Theory]
    [InlineData("2026-09-07T03:59:00Z", "2026-09-06")]
    [InlineData("2026-09-07T04:00:00Z", "2026-09-07")]
    [InlineData("2026-01-07T04:59:00Z", "2026-01-06")]
    [InlineData("2026-01-07T05:00:00Z", "2026-01-07")]
    [InlineData("2026-03-29T04:00:00Z", "2026-03-29")]
    [InlineData("2026-10-25T05:00:00Z", "2026-10-25")]
    public void DueDate_UsesPragueCalendarIncludingSummerTime(string utc, string expected)
        => Assert.Equal(DateOnly.Parse(expected), new ScreeningSchedule().DueDate(DateTimeOffset.Parse(utc)));

    [Fact]
    public void DueDate_EveningSchedule_CatchesUpPreviousDayBeforeScheduledTime()
        => Assert.Equal(new DateOnly(2026, 9, 6), new ScreeningSchedule { Hour = 22, Minute = 30 }.DueDate(DateTimeOffset.Parse("2026-09-07T20:29:00Z")));
}
