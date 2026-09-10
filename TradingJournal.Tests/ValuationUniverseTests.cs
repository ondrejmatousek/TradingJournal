using TradingJournal.Services.Screening;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Tests;

public sealed class ValuationUniverseTests
{
    public static IEnumerable<object[]> Universe => InvestmentUniverse.All.Select(s => new object[] { s.Symbol, s.Sector });

    // Shared synthetic statements deliberately isolate routing from provider availability.
    // This is not evidence that live filings are available for every issuer.
    [Theory]
    [MemberData(nameof(Universe))]
    public void EveryListedTickerHasAnExplicitResult(string symbol, string sector)
    {
        var r = Calculate(symbol, sector, ValuationV2Tests.Dataset());
        Assert.Equal(symbol, r.Symbol);
        Assert.Equal("ValuationV2.0", r.ModelVersion);
        if (sector is "Financial Services" or "Real Estate")
        {
            Assert.Equal("UnsupportedCompanyType", r.Status);
            Assert.Null(r.BaseFairValue);
            Assert.NotEmpty(r.Warnings);
        }
        else
        {
            Assert.NotNull(r.BaseFairValue);
            Assert.Equal(Calculate("UNLISTED.NEW", sector, ValuationV2Tests.Dataset()).BaseFairValue, r.BaseFairValue);
            Assert.All(r.Scenarios, s => Assert.Equal(10, s.Years.Count));
        }
    }

    [Theory]
    [MemberData(nameof(Universe))]
    public void MissingProviderDataCannotCreateAFabricatedValue(string symbol, string sector)
    {
        var r = Calculate(symbol, sector, null);
        Assert.Null(r.BaseFairValue);
        Assert.Null(r.BuyBelow);
        Assert.NotEmpty(r.Warnings);
    }

    [Theory]
    [InlineData(0.001)] [InlineData(1000)] [InlineData(1000000)]
    public void CompanySizeDoesNotChangePerShareValueWhenAllAmountsScale(double factor)
    {
        var original = Calculate("SMALL", "Industrials", ValuationV2Tests.Dataset());
        var d = ValuationV2Tests.Dataset();
        foreach (var p in d.History)
            foreach (var key in p.Facts.Keys.ToArray()) p.Facts[key] = p.Facts[key] with { Value = p.Facts[key].Value * (decimal)factor };
        d = d with { ActualShares = d.ActualShares! with { Value = d.ActualShares.Value * (decimal)factor } };
        var scaled = Calculate("LARGE", "Industrials", d);
        Assert.InRange(Math.Abs(original.BaseFairValue!.Value - scaled.BaseFairValue!.Value), 0, 0.00000001m);
    }

    [Fact]
    public void FutureHistoricalFilingCannotEnterForecast()
    {
        var d = ValuationV2Tests.Dataset();
        d.History[0].Facts["Revenue"] = d.History[0].Facts["Revenue"] with { Filed = new(2027, 1, 1) };
        Assert.Equal("InvalidData", Calculate("NEWCO", "Industrials", d).Status);
    }

    [Theory]
    [InlineData("EUR")] [InlineData("JPY")]
    public void UnsupportedCurrencyIsNotSilentlyTreatedAsUsd(string currency)
    {
        var r = Calculate("FOREIGN", "Industrials", ValuationV2Tests.Dataset() with { Currency = currency });
        Assert.Null(r.BaseFairValue);
        Assert.Equal("UnsupportedCompanyType", r.Status);
    }

    [Theory]
    [InlineData("")] [InlineData("Unknown")] [InlineData("banks")] [InlineData("reit")]
    public void MissingOrFinancialClassificationCannotFallThroughToIndustrialModel(string sector)
        => Assert.Equal("UnsupportedCompanyType", Calculate("NEWCO", sector, ValuationV2Tests.Dataset()).Status);

    [Fact]
    public void ClassificationChangeProducesNewAuditIdentity()
    {
        var d = ValuationV2Tests.Dataset();
        Assert.NotEqual(Calculate("NEWCO", "Bank", d).Id, Calculate("NEWCO", "Industrials", d).Id);
    }

    [Fact]
    public async Task ValuationInputsSurviveMissingLegacyQualityMetrics()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","aapl-fy2025-facts.json")))!;
        var gaap = json["facts"]!["us-gaap"]!.AsObject();
        gaap.Remove("Assets"); gaap.Remove("Liabilities");
        using var doc = System.Text.Json.JsonDocument.Parse(json.ToJsonString());
        var parsed = ScreeningDataProvider.ParseCompanyFinancials(doc.RootElement,"https://example.com/fixture",new(2026,9,10));
        Assert.Null(parsed.Fundamentals);
        Assert.NotNull(parsed.Financials.Latest);
        var r = ValuationV2Tests.Engine().Calculate("NEWCO","Industrials",parsed.Financials,315,new(2026,9,9),new(2026,9,10));
        Assert.NotNull(r.BaseFairValue);
    }

    private static ValuationReport Calculate(string symbol, string sector, FinancialDataset? data)
        => ValuationV2Tests.Engine().Calculate(symbol, sector, data, 15, new(2026, 2, 28), new(2026, 3, 1));
}
