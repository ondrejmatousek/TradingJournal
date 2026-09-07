using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public sealed class ScreeningMacroFallbackTests
{
    [Fact]
    public void Treasury_UsesTenYearYieldAndIgnoresFutureObservations()
    {
        const string xml = "<feed><properties><NEW_DATE>2026-05-01</NEW_DATE><BC_10YEAR>4.5</BC_10YEAR></properties><properties><NEW_DATE>2026-08-01</NEW_DATE><BC_10YEAR>4.1</BC_10YEAR></properties><properties><NEW_DATE>2027-01-01</NEW_DATE><BC_10YEAR>9</BC_10YEAR></properties></feed>";
        var result = ScreeningDataProvider.ParseOfficialMacro("DGS10", xml, new(2026, 9, 7));
        Assert.Equal(4.1m, result.Value);
        Assert.Equal(4.5m, result.PriorValue);
        Assert.Contains("treasury.gov", result.Source);
    }

    [Fact]
    public void Bls_UsesUnemploymentSeriesAndIgnoresAnnualAverage()
    {
        const string json = """
        {"status":"REQUEST_SUCCEEDED","Results":{"series":[{"seriesID":"LNS14000000","data":[{"year":"2026","period":"M08","value":"4.1"},{"year":"2026","period":"M05","value":"4.3"},{"year":"2026","period":"M13","value":"9"}]}]}}
        """;
        var result = ScreeningDataProvider.ParseOfficialMacro("UNRATE", json, new(2026, 9, 7));
        Assert.Equal(4.1m, result.Value);
        Assert.Equal(4.3m, result.PriorValue);
        Assert.Contains("bls.gov", result.Source);
    }
}
