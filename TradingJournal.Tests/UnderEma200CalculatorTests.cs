using TradingJournal.Services;
using TradingJournal.Models;

namespace TradingJournal.Tests;

public class UnderEma200CalculatorTests
{
    [Fact]
    public void CalculateEma_ReturnsConstantValue_ForFlatSeries()
    {
        var values = Enumerable.Repeat(125m, 240).ToList();

        var ema = UnderEma200Calculator.CalculateEma(values, 200);

        Assert.Equal(values.Count, ema.Count);
        Assert.All(ema, value => Assert.Equal(125m, value));
    }

    [Fact]
    public void CalculatePerformance_ReturnsPercentageMove()
    {
        var performance = UnderEma200Calculator.CalculatePerformance(120m, 100m);

        Assert.Equal(20m, performance);
    }

    [Fact]
    public void EvaluateQualification_RequiresPriceBelowEmaAndLongTermGrowth()
    {
        var qualified = UnderEma200Calculator.EvaluateQualification(
            isBelowEma200: true,
            timeframe: EmaTimeframe.Daily,
            performance3YearPercent: 15m,
            performance5YearPercent: 45m,
            distanceFromEmaPercent: -12m,
            dailyPriceCount: UnderEma200Calculator.FiveYearTradingDays);

        var failed = UnderEma200Calculator.EvaluateQualification(
            isBelowEma200: false,
            timeframe: EmaTimeframe.Daily,
            performance3YearPercent: 15m,
            performance5YearPercent: 45m,
            distanceFromEmaPercent: 8m,
            dailyPriceCount: UnderEma200Calculator.FiveYearTradingDays);

        Assert.True(qualified.IsQualified);
        Assert.False(failed.IsQualified);
        Assert.Contains("cena neni pod Daily EMA200", failed.FailureReasons);
    }

    [Fact]
    public void ParseTimeframe_DefaultsToDaily_WhenValueIsMissingOrInvalid()
    {
        Assert.Equal(EmaTimeframe.Daily, EmaTimeframeParser.Parse(null));
        Assert.Equal(EmaTimeframe.Daily, EmaTimeframeParser.Parse(""));
        Assert.Equal(EmaTimeframe.Daily, EmaTimeframeParser.Parse("nonsense"));
        Assert.Equal(EmaTimeframe.Weekly, EmaTimeframeParser.Parse("Weekly"));
        Assert.Equal(EmaTimeframe.Daily, EmaTimeframeParser.Parse("daily"));
    }

    [Fact]
    public void BuildEmaSourcePrices_Weekly_UsesLastCloseOfCompletedWeeks()
    {
        var prices = new List<UnderEma200PricePoint>
        {
            new(new DateTime(2026, 7, 6), 100m),
            new(new DateTime(2026, 7, 8), 102m),
            new(new DateTime(2026, 7, 10), 105m),
            new(new DateTime(2026, 7, 13), 107m),
            new(new DateTime(2026, 7, 17), 111m),
            new(new DateTime(2026, 7, 20), 115m),
            new(new DateTime(2026, 7, 21), 120m)
        };

        var weekly = UnderEma200Calculator.BuildEmaSourcePrices(prices, EmaTimeframe.Weekly);

        Assert.Equal([105m, 111m], weekly.Select(point => point.Close));
        Assert.DoesNotContain(120m, weekly.Select(point => point.Close));
    }

    [Fact]
    public void Analyze_Weekly_UsesWeeklyCloseSeriesForEma200()
    {
        var prices = BuildWeeklySyntheticPrices(
            completedWeeks: 260,
            daysInCurrentWeek: 2,
            start: 100m,
            weeklyStep: 1m,
            currentClose: 250m);

        var analysis = UnderEma200Calculator.Analyze(prices, "TEST", "Test Corp", "Technology", "test", EmaTimeframe.Weekly);
        var weeklyCloses = UnderEma200Calculator
            .BuildEmaSourcePrices(prices.OrderBy(point => point.Date).ToList(), EmaTimeframe.Weekly)
            .Select(point => point.Close)
            .ToList();
        var expectedWeeklyEma = UnderEma200Calculator.CalculateEma(weeklyCloses, UnderEma200Calculator.EmaPeriod)[^1];

        Assert.Equal(EmaTimeframe.Weekly, analysis.Timeframe);
        Assert.Equal(expectedWeeklyEma, analysis.Ema200);
        Assert.Equal(prices.Max(point => point.Date), analysis.LastPriceDate);
    }

    [Fact]
    public void Analyze_RejectsStockAboveDailyEma200()
    {
        var prices = BuildSyntheticPrices(
            count: UnderEma200Calculator.FiveYearTradingDays + UnderEma200Calculator.EmaPeriod,
            start: 100m,
            step: 0.2m,
            lastCloseOverride: 400m);

        var analysis = UnderEma200Calculator.Analyze(prices, "TEST", "Test Corp", "Technology", "test");

        Assert.NotNull(analysis);
        Assert.False(analysis.IsQualified);
        Assert.Contains("cena neni pod Daily EMA200", analysis.FailureReasons);
    }

    [Fact]
    public void CalculateScore_RewardsBetterLongTermGrowthAndConstructivePullback()
    {
        var weak = UnderEma200Calculator.CalculateScore(-34m, -30m, 2m, 21m);
        var strong = UnderEma200Calculator.CalculateScore(-12m, 15m, 45m, 120m);

        Assert.InRange(weak, 0, 100);
        Assert.InRange(strong, 0, 100);
        Assert.True(strong > weak);
    }

    private static List<UnderEma200PricePoint> BuildSyntheticPrices(int count, decimal start, decimal step, decimal lastCloseOverride)
    {
        var prices = new List<UnderEma200PricePoint>(count);
        var date = new DateTime(2020, 1, 1);

        for (var i = 0; i < count; i++)
        {
            prices.Add(new UnderEma200PricePoint(date.AddDays(i), start + (step * i)));
        }

        prices[^1] = prices[^1] with { Close = lastCloseOverride };
        return prices;
    }

    private static List<UnderEma200PricePoint> BuildWeeklySyntheticPrices(
        int completedWeeks,
        int daysInCurrentWeek,
        decimal start,
        decimal weeklyStep,
        decimal currentClose)
    {
        var prices = new List<UnderEma200PricePoint>();
        var monday = new DateTime(2022, 7, 18);

        for (var week = 0; week < completedWeeks; week++)
        {
            var weekStart = monday.AddDays(week * 7);
            var weeklyClose = start + (week * weeklyStep);
            prices.Add(new UnderEma200PricePoint(weekStart, weeklyClose - 4m));
            prices.Add(new UnderEma200PricePoint(weekStart.AddDays(1), weeklyClose - 3m));
            prices.Add(new UnderEma200PricePoint(weekStart.AddDays(2), weeklyClose - 2m));
            prices.Add(new UnderEma200PricePoint(weekStart.AddDays(3), weeklyClose - 1m));
            prices.Add(new UnderEma200PricePoint(weekStart.AddDays(4), weeklyClose));
        }

        var currentWeekStart = monday.AddDays(completedWeeks * 7);
        for (var i = 0; i < daysInCurrentWeek; i++)
        {
            prices.Add(new UnderEma200PricePoint(currentWeekStart.AddDays(i), currentClose));
        }

        return prices;
    }
}
