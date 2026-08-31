using TradingJournal.Services;
using TradingJournal.ViewModels;

namespace TradingJournal.Tests;

public class CotDataServiceTests
{
    [Theory]
    [InlineData("EUR/USD", "EUR/USD")]
    [InlineData("6E", "EUR/USD")]
    [InlineData("EUROFX", "EUR/USD")]
    [InlineData("6B", "GBP/USD")]
    [InlineData("XAU/USD", "XAU/USD")]
    [InlineData("MGC", "XAU/USD")]
    [InlineData("SI", "SILVER")]
    [InlineData("CL", "WTI")]
    [InlineData("ZC", "CORN")]
    [InlineData("MNQ", "NQ")]
    [InlineData("MES", "ES")]
    public void NormalizeSymbol_maps_supported_aliases(string input, string expected)
    {
        Assert.Equal(expected, CotDataService.NormalizeSymbol(input));
    }

    [Fact]
    public void MarketDefinitions_include_only_unique_direct_cot_symbols()
    {
        var symbolKeys = CotDataService.MarketDefinitions.Select(definition => definition.SymbolKey).ToList();

        Assert.Equal(symbolKeys.Count, symbolKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(CotDataService.MarketDefinitions, definition => definition.SymbolKey == "WTI");
        Assert.Contains(CotDataService.MarketDefinitions, definition => definition.SymbolKey == "CORN");
        Assert.Contains(CotDataService.MarketDefinitions, definition => definition.SymbolKey == "SILVER");
        Assert.DoesNotContain(CotDataService.MarketDefinitions, definition => definition.SymbolKey == "AAPL");
    }

    [Fact]
    public void DirectionAssessment_marks_counter_bias_as_weakness_not_no_trade()
    {
        var context = new CotContextViewModel { Bias = "Bullish" };

        var assessment = context.DirectionAssessment("SHORT");

        Assert.Contains("není to automaticky no-trade", assessment, StringComparison.OrdinalIgnoreCase);
    }
}
