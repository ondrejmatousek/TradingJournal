using TradingJournal.Services.Screening;
namespace TradingJournal.Tests;

public class GeopoliticalRssTests
{
    [Fact]
    public void RssUsesRecentUniqueTitlesAndWholeWords()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        string Item(string title, DateTimeOffset date) => $"<item><title>{title}</title><pubDate>{date:R}</pubDate></item>";
        var xml = "<rss><channel>" + string.Concat(new[] { "War escalates", "New tariffs", "Awards announced", "Election result", "New agreement", "New agreement" }.Select(t => Item(t, now)))
            + Item("Military crisis", now.AddDays(-8)) + Item("Attack", now.AddDays(1)) + "</channel></rss>";
        var result = ScreeningDataProvider.ParseGeopoliticalRss(xml, now);
        Assert.Equal(60m, result.Value);
        Assert.Equal(new DateOnly(2026, 9, 9), result.Date);
        Assert.Contains("bbci.co.uk", result.Source);
    }
    [Fact]
    public void EmptyFeedDoesNotGenerateNeutralScore() => Assert.Throws<InvalidDataException>(() =>
        ScreeningDataProvider.ParseGeopoliticalRss("<rss><channel /></rss>", DateTimeOffset.UtcNow));
}
