using System.Net;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TradingJournal.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

/// <summary>Real SQLite and file storage, deterministic HTTP/history doubles; never calls live providers.</summary>
public sealed class ScreeningServiceTests
{
    [Fact]
    public async Task BackgroundMarketRefresh_DoesNotReadDatabaseOrPersonalResearch()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Interceptor.ErrorFactory = () => new InvalidOperationException("No database queries during shared market refresh");
        fixture.History.Result = [new(fixture.Today.ToDateTime(TimeOnly.MinValue), 120)];
        var result = await fixture.Service.RefreshMarketAsync("MSFT", default);
        Assert.Equal(120m, result.Prices.Single().Close);
        Assert.Equal(120m, (await fixture.Store.ReadAsync<ScreeningMarketCache>("market-MSFT", default))!.Prices.Single().Close);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Scheduler_DisabledOrAlreadyCompleted_DoesNotResolveDownloadServices(bool enabled)
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var schedule = new ScreeningSchedule { Enabled = enabled };
        var saved = new ScreeningUpdateRun(schedule.DueDate(now), now, now, 200, 12, true);
        await fixture.Store.WriteAsync(ScreeningUpdateHostedService.StateKey, saved, default);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new ScreeningUpdateHostedService(services.GetRequiredService<IServiceScopeFactory>(), fixture.Store,
            Options.Create(schedule), NullLogger<ScreeningUpdateHostedService>.Instance);
        await worker.RunIfDueAsync(now, default);
        Assert.Equal(saved, await fixture.Store.ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, default));
        Assert.Equal(0, fixture.Http.Calls);
    }

    [Fact]
    public async Task Scheduler_InterruptedRunIsRetriedAndCancellationDoesNotMarkItCompleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Store.WriteAsync(ScreeningUpdateHostedService.StateKey,
            new ScreeningUpdateRun(new ScreeningSchedule().DueDate(now), now.AddHours(-1), null, 15, 0, false), default);
        using var cancellation = new CancellationTokenSource();
        fixture.Http.Respond = _ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
        using var services = new ServiceCollection().AddSingleton(fixture.Provider).AddSingleton(fixture.Service).BuildServiceProvider();
        using var worker = new ScreeningUpdateHostedService(services.GetRequiredService<IServiceScopeFactory>(), fixture.Store,
            Options.Create(new ScreeningSchedule()), NullLogger<ScreeningUpdateHostedService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunIfDueAsync(now, cancellation.Token));
        var state = await fixture.Store.ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, default);
        Assert.Null(state!.CompletedAt);
        Assert.Equal(now, state.StartedAt);
        Assert.Equal(1, fixture.Http.Calls);
    }

    [Fact]
    public async Task ExplicitRefresh_RetriesRecentFailureWithoutManualCacheRemoval()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.WriteAsync("market-MSFT", new ScreeningMarketCache(DateTimeOffset.UtcNow, [new(fixture.Today, 100)], null, "Previous failure"), default);
        fixture.History.Result = [new(fixture.Today.ToDateTime(TimeOnly.MinValue), 120)];
        var response = Assert.IsType<JsonResult>(await Controller(fixture).Refresh("MSFT", default));
        Assert.Equal(120m, Assert.IsType<ScreeningRow>(response.Value).Price);
        Assert.Equal(1, fixture.History.Calls);
        Assert.Equal(1, fixture.Http.Calls);
    }

    [Theory]
    [InlineData("AAPL", 2)]
    [InlineData("WRONG", 1)]
    public async Task BundledIdentifier_MustMatchOfficialSubmissionBeforeLoadingFacts(string listedTicker, int calls)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.WriteAsync("sec-tickers-fallback-v1", new Dictionary<string, long> { ["AAPL"] = 320193 }, default);
        fixture.Http.Respond = r => r.RequestUri!.AbsolutePath.Contains("submissions")
            ? "{\"tickers\":[\"" + listedTicker + "\"]}" : "{\"facts\":{}}";
        await fixture.Provider.FundamentalsAsync("AAPL", default);
        Assert.Equal(calls, fixture.Http.Calls);
        var verified = await fixture.Store.ReadAsync<Dictionary<string, long>>("sec-tickers-v1", default);
        Assert.Equal(listedTicker == "AAPL", verified!.ContainsKey("AAPL"));
    }

    private static ScreeningController Controller(Fixture fixture) => new(fixture.Service, fixture.Store, fixture.Provider)
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test")) } }
    };

    [Fact]
    public async Task Data_ClientDisconnectDuringDatabaseRead_IsHandledAsCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Interceptor.ErrorFactory = () => { cancellation.Cancel(); return new TaskCanceledException("Request disconnected"); };

        var response = Assert.IsType<StatusCodeResult>(await Controller(fixture).Data(cancellation.Token));

        Assert.Equal(499, response.StatusCode);
        Assert.Equal(0, fixture.Http.Calls);
        Assert.Null(await fixture.Store.ReadAsync<ScreeningMarketCache>("market-MSFT", default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Data_DatabaseFailureWithoutClientCancellation_ReturnsRetryableError(bool timeout)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Interceptor.ErrorFactory = () => timeout ? new TaskCanceledException("Provider timeout") : new SqliteException("Database unavailable", 1);

        var response = Assert.IsType<ObjectResult>(await Controller(fixture).Data(default));

        Assert.Equal(503, response.StatusCode);
        Assert.Contains("načíst", Assert.IsType<string>(response.Value));
        Assert.Equal(0, fixture.Http.Calls);
    }

    [Fact]
    public async Task Data_AlreadyCanceledRequest_DoesNotStartDatabaseWork()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Interceptor.ErrorFactory = () => new InvalidOperationException("Must not execute query");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var response = Assert.IsType<StatusCodeResult>(await Controller(fixture).Data(cancellation.Token));
        Assert.Equal(499, response.StatusCode);
    }

    [Theory]
    [InlineData(60, null, 0)]
    [InlineData(780, null, 1)]
    [InlineData(10, "Provider failure", 0)]
    [InlineData(20, "Provider failure", 1)]
    public async Task Refresh_RespectsDifferentLifetimesForSuccessAndFailure(int ageMinutes, string? message, int expectedCalls)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.WriteAsync("market-MSFT", new ScreeningMarketCache(DateTimeOffset.UtcNow.AddMinutes(-ageMinutes), [new(fixture.Today, 100)], null, message), default);
        fixture.History.Result = [new(fixture.Today.ToDateTime(TimeOnly.MinValue), 120)];

        var result = await fixture.Service.RefreshAsync("MSFT", "user", default);

        Assert.Equal(expectedCalls, fixture.History.Calls);
        Assert.Equal(expectedCalls, fixture.Http.Calls);
        Assert.Equal(expectedCalls == 0 ? 100m : 120m, result.Price);
        var persisted = await fixture.Store.ReadAsync<ScreeningMarketCache>("market-MSFT", default);
        Assert.Equal(result.Price, persisted!.Prices.Single().Close);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_PreservesOldPricesAndFundamentalsWhenProvidersFail(bool timeout)
    {
        await using var fixture = await Fixture.CreateAsync();
        var old = new ScreeningMarketCache(DateTimeOffset.UtcNow.AddDays(-1), [new(fixture.Today.AddDays(-1), 100)], fixture.Fundamentals, null);
        await fixture.Store.WriteAsync("market-MSFT", old, default);
        if (timeout) fixture.History.Error = new TaskCanceledException("Provider timeout, not caller cancellation");
        // HTTP responds successfully with no usable SEC facts. Missing values must not erase old facts.
        var result = await fixture.Service.RefreshAsync("MSFT", "user", default);

        Assert.Equal(100m, result.Price);
        Assert.Equal(old.Fundamentals, result.Fundamentals);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshMessage));
        var persisted = await fixture.Store.ReadAsync<ScreeningMarketCache>("market-MSFT", default);
        Assert.Equal(old.Prices, persisted!.Prices);
        Assert.Equal(old.Fundamentals, persisted.Fundamentals);
        Assert.True(persisted.RetrievedAt > old.RetrievedAt);
    }

    [Fact]
    public async Task Refresh_CallerCancellationDoesNotOverwriteCache_AndReleasesGate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var old = new ScreeningMarketCache(DateTimeOffset.UtcNow.AddDays(-1), [new(fixture.Today, 100)], null, null);
        await fixture.Store.WriteAsync("market-MSFT", old, default);
        using var cancellation = new CancellationTokenSource();
        fixture.History.BeforeReturn = () => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.RefreshAsync("MSFT", "user", cancellation.Token));
        var persisted = await fixture.Store.ReadAsync<ScreeningMarketCache>("market-MSFT", default);
        Assert.Equal(old.RetrievedAt, persisted!.RetrievedAt);
        Assert.Equal(100m, persisted.Prices.Single().Close);
        Assert.Equal(0, fixture.Http.Calls);

        fixture.History.BeforeReturn = null;
        fixture.History.Result = [new(fixture.Today.ToDateTime(TimeOnly.MinValue), 110)];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(110m, (await fixture.Service.RefreshAsync("MSFT", "user", deadline.Token)).Price);
    }

    [Fact]
    public async Task Load_UsesTradingSeasonalityWithoutExternalCalls_AndKeepsWholeUniverse()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedHistoryAsync();

        var rows = await fixture.Service.LoadAsync("user", default);

        Assert.Equal(InvestmentUniverse.All.Count, rows.Count);
        var microsoft = Assert.Single(rows, r => r.Symbol == "MSFT");
        Assert.Equal(100m, microsoft.Price);
        Assert.True(microsoft.Seasonality[1].Years >= 5);
        Assert.Null(microsoft.Score);
        Assert.Equal(0, fixture.Http.Calls);
        Assert.Equal(0, fixture.History.Calls);
    }

    [Fact]
    public async Task Load_DoesNotSpliceHistoriesWithDifferentPriceAdjustmentBases()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedHistoryAsync();
        await fixture.Store.WriteAsync("market-MSFT", new ScreeningMarketCache(DateTimeOffset.UtcNow, [new(fixture.Today, 10)], null, null), default);

        var row = Assert.Single(await fixture.Service.LoadAsync("user", default), r => r.Symbol == "MSFT");

        Assert.Equal(10m, row.Price);
        Assert.Equal(0, row.Seasonality[1].Years);
        Assert.Null(row.Pillars.Single(p => p.Key == "technical").Score);
    }

    [Fact]
    public async Task Load_PersonalFundamentalsOverrideSec_AndDeletingOverrideRestoresSec()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.WriteAsync("market-MSFT", new ScreeningMarketCache(DateTimeOffset.UtcNow, [new(fixture.Today, 100)], fixture.Fundamentals, null), default);
        var custom = fixture.Fundamentals with { OperatingCashFlow = 500 };
        await fixture.Store.WriteAsync("research-first-MSFT", new ScreeningResearch { Fundamentals = custom }, default);

        var first = Assert.Single(await fixture.Service.LoadAsync("first", default), r => r.Symbol == "MSFT");
        var second = Assert.Single(await fixture.Service.LoadAsync("second", default), r => r.Symbol == "MSFT");
        Assert.Equal(custom, first.Fundamentals);
        Assert.Equal(fixture.Fundamentals, second.Fundamentals);
        Assert.True(first.Valuation!.Base > second.Valuation!.Base);

        await fixture.Store.WriteAsync("research-first-MSFT", new ScreeningResearch(), default);
        var restored = Assert.Single(await fixture.Service.LoadAsync("first", default), r => r.Symbol == "MSFT");
        Assert.Equal(second.Valuation, restored.Valuation);
    }

    [Fact]
    public async Task Macro_ParsesMissingObservationsAndUsesThreeMonthComparison()
    {
        await using var fixture = await Fixture.CreateAsync();
        var today = fixture.Today;
        fixture.Http.Respond = _ => $"observation_date,value\n{today.AddMonths(-4):yyyy-MM-dd},3.5\n{today.AddMonths(-3):yyyy-MM-dd},4.0\n{today.AddDays(-1):yyyy-MM-dd},.\n{today:yyyy-MM-dd},4.5\n{today.AddDays(1):yyyy-MM-dd},999\n";

        var result = await fixture.Provider.MacroAsync(default);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => { Assert.Equal(today, r.Date); Assert.Equal(4.5m, r.Value); Assert.Equal(4m, r.PriorValue); });
        var persisted = await fixture.Store.ReadAsync<List<MacroObservation>>("macro-v1", default);
        Assert.Equal(result, persisted);
    }

    [Fact]
    public async Task Macro_IncompleteRefreshDoesNotErasePreviousObservations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var previous = new List<MacroObservation> { new("DGS10", fixture.Today.AddDays(-1), 4, 3, "https://fred.stlouisfed.org/series/DGS10") };
        await fixture.Store.WriteAsync("macro-v1", previous, default);
        fixture.Http.Respond = request => request.RequestUri!.Query.Contains("UNRATE") ? "observation_date,value\n" : $"observation_date,value\n{fixture.Today:yyyy-MM-dd},4\n";

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provider.MacroAsync(default));

        Assert.Equal(previous, await fixture.Store.ReadAsync<List<MacroObservation>>("macro-v1", default));
    }

    private sealed class HistoryDouble : IUnderEma200HistoryProvider
    {
        public int Calls { get; private set; }
        public IReadOnlyList<UnderEma200PricePoint> Result { get; set; } = [];
        public Exception? Error { get; set; }
        public Action? BeforeReturn { get; set; }
        public Task<IReadOnlyList<UnderEma200PricePoint>> GetDailyPricesAsync(string symbol, CancellationToken ct)
        {
            Calls++;
            BeforeReturn?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Error == null ? Task.FromResult(Result) : Task.FromException<IReadOnlyList<UnderEma200PricePoint>>(Error);
        }
    }
    private sealed class HttpDouble : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, string> Respond { get; set; } = _ => "{\"facts\":{}}";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Respond(request)) });
        }
    }
    private sealed class EnvironmentDouble : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "screening-service-tests", Guid.NewGuid().ToString("N"));
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class DatabaseDouble(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property.GetColumnType()?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true) property.SetColumnType("TEXT");
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)) property.SetColumnType("REAL");
            }
        }
    }
    private sealed class QueryInterceptor : DbCommandInterceptor
    {
        public Func<Exception>? ErrorFactory { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (ErrorFactory != null) throw ErrorFactory();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly EnvironmentDouble environment = new();
        private readonly SqliteConnection connection = new("DataSource=:memory:");
        private ApplicationDbContext db = null!;
        public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);
        public FundamentalInput Fundamentals => new(Today.AddMonths(-8), Today.AddMonths(-6), 1100, 1000, 250, 50, 150, 1500, 500, 100, "https://www.sec.gov/test");
        public ScreeningStore Store { get; private set; } = null!;
        public HistoryDouble History { get; } = new();
        public HttpDouble Http { get; } = new();
        public QueryInterceptor Interceptor { get; } = new();
        public ScreeningDataProvider Provider { get; private set; } = null!;
        public InvestmentScreeningService Service { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.connection.OpenAsync();
            fixture.db = new DatabaseDouble(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(fixture.connection).AddInterceptors(fixture.Interceptor).Options);
            await fixture.db.Database.EnsureCreatedAsync();
            fixture.Store = new ScreeningStore(fixture.environment);
            await fixture.Store.WriteAsync("sec-tickers-v1", new Dictionary<string, long> { ["MSFT"] = 789019 }, default);
            fixture.Provider = new(fixture.Http, new ConfigurationBuilder().Build(), fixture.Store);
            fixture.Service = new(fixture.db, fixture.Store, fixture.Provider, fixture.History);
            return fixture;
        }
        public async Task SeedHistoryAsync()
        {
            for (var date = Today.AddYears(-8); date < Today; date = date.AddDays(1))
                if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                    db.SeasonalityPrices.Add(new() { AssetKey = "MSFT", Date = date, Close = 100, Source = "test", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await connection.DisposeAsync();
            Http.Dispose();
            // Only remove this fixture's generated directory, never an application data directory.
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "screening-service-tests")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(environment.ContentRootPath);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
