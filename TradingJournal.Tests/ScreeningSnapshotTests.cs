using System.Text.Json;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public class ScreeningSnapshotTests
{
    [Fact]
    public void FiltersAndSortsEntireSnapshotBeforePaging()
    {
        var rows = Enumerable.Range(1, 60).Select(i => new ScreeningRow { Symbol = $"T{i:000}", Discount = i, Score = i, Sector = "Tech" }).ToList();
        var snapshot = new ScreeningSnapshot(DateTimeOffset.UtcNow, rows, []);
        var result = JsonSerializer.SerializeToElement(ScreeningSnapshot.Page(snapshot, 2, 25, null, null, "discount", "discount"));
        Assert.Equal(60, result.GetProperty("total").GetInt32());
        var page = result.GetProperty("rows");
        Assert.Equal(25, page.GetArrayLength());
        Assert.Equal("T035", page[0].GetProperty("Symbol").GetString());
        var searched = JsonSerializer.SerializeToElement(ScreeningSnapshot.Page(snapshot, 1, 25, "T059", null, null, null));
        Assert.Equal(1, searched.GetProperty("total").GetInt32());
    }

    [Fact]
    public void EmptyFilterClampsPageAndKeepsGlobalSummary()
    {
        var snapshot = new ScreeningSnapshot(DateTimeOffset.UtcNow, [new() { Symbol = "A", Score = 75 }], []);
        var result = JsonSerializer.SerializeToElement(ScreeningSnapshot.Page(snapshot, 100, 25, "missing", null, null, null));
        Assert.Equal(1, result.GetProperty("page").GetInt32());
        Assert.Equal(0, result.GetProperty("total").GetInt32());
        Assert.Equal(1, result.GetProperty("rated").GetInt32());
    }

    [Fact]
    public void ValuationFiltersUseMinimumThresholdsAndCanSortByConfidence()
    {
        var rows = new List<ScreeningRow>
        {
            new() { Symbol = "LOW", Discount = 25, ValuationScore = 80, ValuationConfidence = 65 },
            new() { Symbol = "BEST", Discount = 30, ValuationScore = 85, ValuationConfidence = 90 },
            new() { Symbol = "RISK", Discount = 50, ValuationScore = 95, ValuationConfidence = 40 }
        };
        var result = JsonSerializer.SerializeToElement(ScreeningSnapshot.Page(new(DateTimeOffset.UtcNow, rows, []), 1, 25, null, null, null, "confidence", 20, 70, 70));
        Assert.Equal(1, result.GetProperty("total").GetInt32());
        Assert.Equal("BEST", result.GetProperty("rows")[0].GetProperty("Symbol").GetString());
    }
}
