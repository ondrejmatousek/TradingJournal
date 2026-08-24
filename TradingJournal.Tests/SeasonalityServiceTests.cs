using TradingJournal.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace TradingJournal.Tests;

public sealed class SeasonalityServiceTests
{
    [Fact]
    public void ParseYahoo_reads_valid_close_values_and_ignores_nulls()
    {
        const string json = """
        {"chart":{"result":[{"timestamp":[1704067200,1704153600,1704240000],"indicators":{"quote":[{"close":[100.5,null,102.25]}]}}],"error":null}}
        """;

        var prices = SeasonalityService.ParseYahoo(json);

        Assert.Equal(2, prices.Count);
        Assert.Equal(new DateOnly(2024, 1, 1), prices[0].Date);
        Assert.Equal(100.5m, prices[0].Close);
        Assert.Equal(102.25m, prices[1].Close);
    }

    [Fact]
    public void ParseStooq_reads_daily_csv_close_column()
    {
        const string csv = "Date,Open,High,Low,Close,Volume\n2024-01-02,1,2,0.5,1.5,100\n";

        var prices = SeasonalityService.ParseStooq(csv);

        var price = Assert.Single(prices);
        Assert.Equal(new DateOnly(2024, 1, 2), price.Date);
        Assert.Equal(1.5m, price.Close);
    }

    [Fact]
    public async Task LoadAsync_returns_data_for_all_supported_assets_when_provider_responds()
    {
        var service = new SeasonalityService(new FakeHttpClientFactory(), NullLogger<SeasonalityService>.Instance);

        var model = await service.LoadAsync();

        Assert.Equal(4, model.Assets.Count);
        Assert.All(model.Assets, asset => Assert.NotEmpty(model.Prices[asset.Key]));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler());
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json = """
            {"chart":{"result":[{"timestamp":[1704067200,1704153600],"indicators":{"quote":[{"close":[100.5,102.25]}]}}],"error":null}}
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
