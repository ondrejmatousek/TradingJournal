using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TradingJournal.Services.Screening;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Tests;

public sealed class FinancialCoverageTests
{
    [Fact]
    public async Task FmpHonorsRateLimitAcrossEndpointsAndSymbols()
    {
        using var http = new LimitedHttp();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["FinancialModelingPrep:ApiKey"] = Guid.NewGuid().ToString() }).Build();
        var provider = new FmpFinancialProvider(http, config);
        var first = await provider.FetchDetailedAsync("TEST", new(2026,9,16), default);
        var second = await provider.FetchDetailedAsync("OTHER", new(2026,9,16), default);
        Assert.Equal(1, http.Calls);
        Assert.Contains(first.Issues, x => x.Code == "rate-limit");
        Assert.Contains(second.Issues, x => x.Code == "rate-limit");
    }
    [Theory]
    [InlineData(true, false, 30)]
    [InlineData(true, true, 99)]
    [InlineData(false, false, null)]
    public void SecDerivesDaOnlyFromMatchingComponents(bool samePeriod, bool hasTotal, int? expected)
    {
        object Tag(decimal value, string start = "2025-01-01") => new { units = new { USD = new[] { new { start, end="2025-12-31", filed="2026-02-01", val=value } } } };
        var facts = new Dictionary<string, object> { ["Revenues"] = Tag(1000), ["Depreciation"] = Tag(10),
            ["AmortizationOfIntangibleAssets"] = Tag(20, samePeriod ? "2025-01-01" : "2025-07-01") };
        if (hasTotal) facts["DepreciationDepletionAndAmortization"] = Tag(99);
        var root = JsonSerializer.SerializeToElement(new { facts = new Dictionary<string,object> { ["us-gaap"] = facts } });
        var parsed = SecFinancialParser.Parse(root, "SEC", new(2026,9,16));
        Assert.Equal(expected is {} v ? (decimal?)v : null, parsed.Latest!.Facts.GetValueOrDefault("Da")?.Value);
        if (samePeriod && !hasTotal) Assert.Equal("Derived", parsed.Latest.Facts["Da"].Kind);
    }
    private sealed class LimitedHttp : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }
    }
    [Theory]
    [InlineData(true, 75)]
    [InlineData(false, null)]
    public void SecRequiresBothLiabilityComponentsAtTheSameDate(bool matching, int? expected)
    {
        object Instant(int value, string end) => new { units = new { USD = new[] { new { end, filed="2026-02-01", val=value } } } };
        var facts = new Dictionary<string,object> {
            ["Revenues"] = new { units = new { USD = new[] { new { start="2025-01-01", end="2025-12-31", filed="2026-02-01", val=1000 } } } },
            ["LiabilitiesCurrent"] = Instant(25, "2025-12-31"),
            ["LiabilitiesNoncurrent"] = Instant(50, matching ? "2025-12-31" : "2025-09-30") };
        var root = JsonSerializer.SerializeToElement(new { facts = new Dictionary<string,object> { ["us-gaap"] = facts } });
        var result = SecFinancialParser.Parse(root, "SEC", new(2026,9,16));
        Assert.Equal(expected is {} v ? (decimal?)v : null, result.Latest!.Facts.GetValueOrDefault("Liabilities")?.Value);
        if (matching) Assert.Equal("Derived", result.Latest.Facts["Liabilities"].Kind);
    }
    private static JsonElement Statements(string currency = "USD", int filingYear = 2026) => JsonSerializer.SerializeToElement(
        Enumerable.Range(2023, 3).Select(year => new {
            symbol = "TEST", period = "FY", date = $"{year}-09-30", filingDate = $"{Math.Min(year + 1, filingYear)}-02-01",
            reportedCurrency = currency, revenue = 1000, operatingIncome = 200, netIncome = 150,
            incomeBeforeTax = 200, weightedAverageShsOutDil = 100, totalDebt = 0, cashAndCashEquivalents = 0,
            totalAssets = 1000, totalLiabilities = 0, operatingCashFlow = 200, capitalExpenditure = 0,
            depreciationAndAmortization = 0
        }));
    [Fact]
    public void FmpRetainsZeroDebtCashAndCapexAndFiscalDates()
    {
        var rows = Statements();
        var result = FmpFinancialProvider.Parse("TEST", new(2026, 9, 16), rows, rows, rows);
        Assert.NotNull(result.Fundamentals);
        var p = result.Financials!.Latest!;
        Assert.Equal(new DateOnly(2024, 10, 1), p.Start);
        Assert.Equal(0, ValuationMath.Debt(p));
        Assert.Equal(0, p.Facts["CapEx"].Value);
        Assert.Equal(new DateOnly(2026, 2, 1), p.Facts["Revenue"].Filed);
        Assert.Null(result.Financials.ActualShares);
        Assert.True(FinancialDataMerge.HasValuationInputs(result.Financials));
    }
    [Fact]
    public void FmpDoesNotInventFilingDatesOrConvertCurrencies()
    {
        var eur = Statements("EUR");
        var result = FmpFinancialProvider.Parse("TEST", new(2026, 9, 16), eur, eur, eur);
        Assert.Equal("EUR", result.Financials!.Currency);
        Assert.Contains(result.Issues, i => i.Code == "currency");
        var missing = JsonSerializer.SerializeToElement(new[] { new { symbol = "TEST", period = "FY", date = "2025-12-31", reportedCurrency = "USD", revenue = 5 } });
        Assert.Null(FmpFinancialProvider.Parse("TEST", new(2026,9,16), missing, missing, missing).Financials);
        Assert.Null(FmpFinancialProvider.Parse("TEST", new(2024,1,1), eur, eur, eur).Financials);
    }
    [Fact]
    public void PartialEndpointFailureRetainsIncomeHistory()
    {
        var result = FmpFinancialProvider.Parse("TEST", new(2026,9,16), Statements(), default, default);
        Assert.Null(result.Fundamentals);
        Assert.Equal(3, result.Financials!.History.Count);
        Assert.Equal(1000, result.Financials.Latest!.Facts["Revenue"].Value);
    }
    [Fact]
    public void MergePreservesPrimaryZerosAndRefusesDifferentPeriodOrCurrency()
    {
        var rows = Statements();
        var complete = FmpFinancialProvider.Parse("TEST", new(2026,9,16), rows, rows, rows);
        var p = complete.Financials!.Latest!;
        var facts = new Dictionary<string, FinancialFact>(p.Facts);
        facts.Remove("Da");
        var primary = complete with { Financials = complete.Financials with { Latest = p with { Facts = facts } } };
        Assert.True(FinancialDataMerge.Combine(primary, complete).Financials!.Latest!.Facts.ContainsKey("Da"));
        var otherPeriod = complete with { Financials = complete.Financials with { Latest = p with { Start = p.Start.AddDays(7) }, History = [] } };
        Assert.False(FinancialDataMerge.Combine(primary, otherPeriod).Financials!.Latest!.Facts.ContainsKey("Da"));
        var otherCurrency = complete with { Financials = complete.Financials with { Currency = "EUR" } };
        Assert.Contains(FinancialDataMerge.Combine(primary, otherCurrency).Issues, i => i.Code == "currency");
        Assert.Equal(0, FinancialDataMerge.Combine(primary, complete).Financials!.Latest!.Facts["Cash"].Value);
    }
    [Fact]
    public void AuditSeparatesMissingDataFromCompanyRiskAndDetectsHistoryGaps()
    {
        var today = new DateOnly(2026,9,16);
        var prices = Enumerable.Range(0,252).Select(i => new TradingJournal.Models.MarketPrice(today.AddDays(-i * 11), 100)).ToArray();
        var audit = ScreeningCoverage.Assess("TEST", prices, null, null, null, today);
        Assert.Equal("available", audit.Checks.Single(c => c.Key == "price").Status);
        Assert.Equal("missing", audit.Checks.Single(c => c.Key == "history").Status);
        Assert.False(audit.Ready);
        Assert.Equal("Chybí podklady", new ScreeningRow().Decision);
        Assert.Equal("Zvýšené riziko", new ScreeningRow { Entry = new("risk", "", null,null,null,null,[]) }.Decision);
    }
    [Fact]
    public async Task SecFailureStillCallsFmpAndDoesNotLeakApiKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "financial-coverage-" + Guid.NewGuid().ToString("N"));
        var env = new TestEnvironment { ContentRootPath = root };
        try
        {
            var store = new ScreeningStore(env);
            await store.WriteAsync("sec-tickers-v1", new Dictionary<string,long> { ["TEST"] = 1 }, default);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["FinancialModelingPrep:ApiKey"] = "test-secret" }).Build();
            using var http = new FakeHttp();
            var provider = new ScreeningDataProvider(http, config, store, env, new(http, config));
            var result = await provider.FetchFinancialsAsync("TEST", default);
            Assert.Equal(3, http.FmpRequests);
            Assert.NotNull(result.Fundamentals);
            Assert.Contains(result.Issues, i => i.Source == "SEC");
            Assert.DoesNotContain("test-secret", JsonSerializer.Serialize(result));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchFinancialsAsync("TEST", cancel.Token));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
    {
        public int FmpRequests;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.RequestUri!.Host.Contains("sec.gov")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            FmpRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Statements().GetRawText()) });
        }
    }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
