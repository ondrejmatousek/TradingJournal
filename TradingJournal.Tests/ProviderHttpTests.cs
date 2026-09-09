using System.Net;
using TradingJournal.Services;
using TradingJournal.Services.Insiders;

namespace TradingJournal.Tests;

public class ProviderHttpTests
{
    private sealed class Handler(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(reply(++Calls));
        }
    }

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(408)]
    public async Task TemporaryFailure_RetriesAndReturnsRecoveredBody(int status)
    {
        using var handler = new Handler(n => n == 1 ? new((HttpStatusCode)status) : new(HttpStatusCode.OK) { Content = new StringContent("recovered") });
        using var client = new HttpClient(handler);
        Assert.Equal("recovered", await ProviderHttp.GetStringAsync(client, "https://provider.test", default));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task PersistentFailure_StopsAfterThreeAttempts()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => ProviderHttp.GetStringAsync(client, "https://provider.test", default));
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task PermanentFailure_IsNotRetried(int status)
    {
        using var handler = new Handler(_ => new((HttpStatusCode)status));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => ProviderHttp.GetStringAsync(client, "https://provider.test", default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cancellation_StopsRetries()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(_ => { cts.Cancel(); return new(HttpStatusCode.ServiceUnavailable); });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProviderHttp.GetStringAsync(client, "https://provider.test", cts.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("xslF345X05/doc4.xml", "doc4.xml")]
    [InlineData("xslF345X03/ownership.xml", "ownership.xml")]
    [InlineData("doc4.xml", "doc4.xml")]
    public void OwnershipPath_UsesRawXml(string input, string expected) => Assert.Equal(expected, InsiderSecProvider.RawDocumentPath(input));

    [Theory]
    [InlineData("<html><head><meta charset='UTF-8'></head></html>")]
    [InlineData("<error>Access denied</error>")]
    public void OwnershipParser_RejectsNonFilings(string body) => Assert.Throws<InvalidDataException>(() =>
        InsiderSecProvider.ParseOwnershipXml(body, "AAPL", "123", new(2026, 9, 9), null, "4", "https://sec.test"));
}
