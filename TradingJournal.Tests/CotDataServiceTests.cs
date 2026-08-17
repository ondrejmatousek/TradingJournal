using TradingJournal.Services;
using TradingJournal.ViewModels;

namespace TradingJournal.Tests;

public class CotDataServiceTests
{
    [Theory]
    [InlineData("EUR/USD", "EUR/USD")]
    [InlineData("6E", "EUR/USD")]
    [InlineData("EUROFX", "EUR/USD")]
    [InlineData("XAU/USD", "XAU/USD")]
    [InlineData("MGC", "XAU/USD")]
    [InlineData("MNQ", "NQ")]
    [InlineData("MES", "ES")]
    public void NormalizeSymbol_maps_supported_aliases(string input, string expected)
    {
        Assert.Equal(expected, CotDataService.NormalizeSymbol(input));
    }

    [Fact]
    public void DirectionAssessment_marks_counter_bias_as_weakness_not_no_trade()
    {
        var context = new CotContextViewModel { Bias = "Bullish" };

        var assessment = context.DirectionAssessment("SHORT");

        Assert.Contains("není to automaticky no-trade", assessment, StringComparison.OrdinalIgnoreCase);
    }
}
