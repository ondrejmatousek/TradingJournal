using TradingJournal.Models;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public class ScreeningCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 6);
    private static readonly InvestmentSymbol Stock = new("TEST", "Test", "Technology");
    private static FundamentalInput Fundamentals => new(new(2025,12,31), new(2026,2,15), 1100,1000,250,50,150,1500,500,100,"https://www.sec.gov/filing");
    private static List<SeasonalityPrice> Prices()
    {
        var output = new List<SeasonalityPrice>();
        for (var date = Today.AddYears(-16); date <= Today; date = date.AddDays(1))
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) output.Add(new(date, 10 + output.Count * .001m));
        return output;
    }
    [Fact]
    public void Universe_IsBroadUniqueAndDiversified()
    {
        Assert.True(InvestmentUniverse.All.Count >= 150);
        Assert.Equal(InvestmentUniverse.All.Count, InvestmentUniverse.All.Select(s=>s.Symbol).Distinct().Count());
        Assert.Equal(12, InvestmentUniverse.All.Select(s=>s.Sector).Distinct().Count());
    }
    [Fact]
    public void SparseData_NeverProducesInvestmentScore()
    {
        var row = ScreeningCalculator.Evaluate(Stock, Prices(), null, [], new(), Today);
        Assert.Null(row.Score); Assert.Null(row.Valuation); Assert.Equal(20, row.Coverage);
    }
    [Fact]
    public void StalePrices_DisableTechnicalAndValuationScore()
    {
        var row = ScreeningCalculator.Evaluate(Stock, Prices().Where(p=>p.Date<Today.AddDays(-10)).ToArray(), Fundamentals, [], new(), Today);
        Assert.Null(row.Discount); Assert.Null(row.Pillars.Single(p=>p.Key=="technical").Score); Assert.Null(row.Score);
    }
    [Fact]
    public void Dcf_ScenariosAreOrderedAndNoDoubleDebtSubtraction()
    {
        var result = ScreeningCalculator.Value(Fundamentals, "Technology")!;
        Assert.True(result.Low < result.Base && result.Base < result.High);
        Assert.Equal(result, ScreeningCalculator.Value(Fundamentals with { Liabilities = 900 }, "Technology"));
        Assert.Null(ScreeningCalculator.Value(Fundamentals, "Financial Services"));
        Assert.Null(ScreeningCalculator.Value(Fundamentals with { OperatingCashFlow = 0 }, "Technology"));
    }
    [Fact]
    public void Seasonality_ExcludesFutureDataAndRequiresFiveYears()
    {
        var prices = Prices();
        var expected = ScreeningCalculator.Seasonality(prices, Today, 63);
        prices.Add(new(Today.AddYears(1), 10000000));
        Assert.Equal(expected, ScreeningCalculator.Seasonality(prices, Today, 63));
        Assert.InRange(expected.Years, 5, 15); Assert.True(expected.Median > 0); Assert.Equal(100m, expected.WinRate);
        Assert.Null(ScreeningCalculator.Seasonality(prices.Where(p=>p.Date>Today.AddYears(-3)).ToArray(),Today,63).Median);
    }
    [Fact]
    public void Evidence_RejectsFutureExpiredUnverifiedAndDuplicateSources()
    {
        var valid = new ResearchEvidence("analysts",80,Today.AddDays(-10),"https://example.com/analysis","Author","Thesis","Risk",90,true);
        Assert.Equal(80m, ScreeningCalculator.EvidenceScore([valid,valid with{Score=10}],"analysts",Today));
        Assert.Null(ScreeningCalculator.EvidenceScore([valid with{Published=Today.AddDays(1)}],"analysts",Today));
        Assert.Null(ScreeningCalculator.EvidenceScore([valid with{Published=Today.AddDays(-200)}],"analysts",Today));
        Assert.Null(ScreeningCalculator.EvidenceScore([valid with{TrackRecordVerified=false}],"analysts",Today));
        Assert.Null(ScreeningCalculator.EvidenceScore([valid with{SourceUrl="javascript:alert(1)"}],"analysts",Today));
    }
    [Fact]
    public void MissingPillarsAreNotRenormalized()
    {
        var macro = new List<MacroObservation>{new("DGS10",Today.AddDays(-2),4,4,"https://fred.stlouisfed.org"),new("UNRATE",Today.AddDays(-30),4,4,"https://fred.stlouisfed.org")};
        var row = ScreeningCalculator.Evaluate(Stock,Prices(),Fundamentals,macro,new(),Today);
        Assert.Equal(90,row.Coverage);
        Assert.NotNull(row.Score);
        Assert.Equal(Math.Round(row.Pillars.Sum(p=>(p.Score??0)*p.Weight)/100,1),row.Score);
    }
    [Fact]
    public void StaleFundamentals_DoNotCreateQualityOrValue()
    {
        var row = ScreeningCalculator.Evaluate(Stock,Prices(),Fundamentals with{PeriodEnd=Today.AddYears(-3)},[],new(),Today);
        Assert.Null(row.Valuation); Assert.Null(row.Pillars.Single(p=>p.Key=="quality").Score);
    }
}
