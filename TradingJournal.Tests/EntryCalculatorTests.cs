using TradingJournal.Models;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public class EntryCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);
    private static FundamentalInput F => new(Today.AddMonths(-8), Today.AddMonths(-6), 1100, 1000, 250, 50, 150, 1500, 500, 100, "https://example.com");
    private static List<MarketPrice> History(bool falling = false) => Enumerable.Range(0, 252)
        .Select(i => new MarketPrice(Today.AddDays(i - 251), falling ? 100m - i * .1m : 78m)).ToList();
    private static ScreeningRow Row() => new()
    {
        Price = 78, PriceDate = Today, Fundamentals = F,
        Valuation = new(90, 110, 140, 5, 11, 2) { Model = "DCF-10-v2", BuyBelow = 80, MarginOfSafety = 30 },
        Pillars = [new("quality", "Kvalita", 25, 75, "")]
    };
    [Fact]
    public void DiscountAndStableSupportProduceCandidateWithBoundedZoneAndReasons()
    {
        var row = Row();
        var result = EntryCalculator.Evaluate(row, History(), Today);
        Assert.Equal("candidate", result.Status);
        Assert.True(result.ZoneLow <= row.Price && row.Price <= result.ZoneHigh);
        Assert.True(result.ZoneHigh <= row.Valuation!.BuyBelow);
        Assert.True(result.Reasons.Count >= 5);
    }
    [Fact]
    public void FallingCheapStockIsNotBuySignal()
    {
        var row = Row(); var history = History(true); row.Price = history[^1].Close;
        Assert.Equal("wait", EntryCalculator.Evaluate(row, history, Today).Status);
    }
    [Fact]
    public void QualityAndFreshnessAreHardGates()
    {
        var row = Row(); row.Fundamentals = F with { NetIncome = -1 };
        Assert.Equal("risk", EntryCalculator.Evaluate(row, History(), Today).Status);
        row = Row(); row.PriceDate = Today.AddDays(-8);
        Assert.Equal("insufficient", EntryCalculator.Evaluate(row, History(), Today).Status);
        row = Row(); row.Price = 85;
        Assert.Equal("expensive", EntryCalculator.Evaluate(row, History(), Today).Status);
    }
    [Fact]
    public void MissingOrGappedHistoryCannotConfirmEntryAndFutureDataAreIgnored()
    {
        Assert.Equal("insufficient", EntryCalculator.Evaluate(Row(), History().TakeLast(100).ToArray(), Today).Status);
        var history = History(); history[0] = new(Today.AddYears(-2), 78);
        Assert.Equal("insufficient", EntryCalculator.Evaluate(Row(), history, Today).Status);
        history = History(); history.Add(new(Today.AddDays(1), 1));
        Assert.Equal("candidate", EntryCalculator.Evaluate(Row(), history, Today).Status);
    }
    [Fact]
    public void DcfHigherDiscountRateLowersValueAndLimitIsConservative()
    {
        var lowRates = ScreeningCalculator.Value(F, "Technology", 3)!;
        var highRates = ScreeningCalculator.Value(F, "Technology", 6)!;
        Assert.True(highRates.Base < lowRates.Base);
        Assert.True(highRates.Low < highRates.Base && highRates.Base < highRates.High);
        Assert.True(highRates.BuyBelow <= highRates.Low);
        Assert.True(highRates.BuyBelow <= highRates.Base * (1 - highRates.MarginOfSafety / 100));
        Assert.InRange(highRates.MarginOfSafety, 25, 40);
        Assert.Null(ScreeningCalculator.Value(F, "Real Estate"));
    }
    [Fact]
    public void EvaluateBuildsPersistableExplanationWithoutMacroOrNews()
    {
        var row = ScreeningCalculator.Evaluate(new("TEST", "Test", "Technology"), History(), F, [], new(), Today);
        var json = System.Text.Json.JsonSerializer.Serialize(row);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ScreeningRow>(json)!;
        Assert.Equal("DCF-5-composite", restored.Valuation!.Model);
        Assert.NotEmpty(restored.Entry!.Reasons);
        Assert.Equal(row.Entry!.Status, restored.Entry.Status);
    }
}
