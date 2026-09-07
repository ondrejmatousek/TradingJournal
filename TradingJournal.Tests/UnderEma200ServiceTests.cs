using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradingJournal.Controllers;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services;
using TradingJournal.ViewModels;

namespace TradingJournal.Tests;

public class UnderEma200ServiceTests
{
    [Fact]
    public async Task RefreshAsync_SavesQualifiedAndRejectedBelowEmaSnapshots()
    {
        await using var database = await CreateDatabaseAsync();
        var provider = new FakeHistoryProvider(new Dictionary<string, IReadOnlyList<UnderEma200PricePoint>>
        {
            ["MCD"] = BuildGrowthPullbackPrices(lastPriceFactor: 0.92m),
            ["MSFT"] = BuildGrowthPullbackPrices(lastPriceFactor: 0.60m)
        });
        var service = new UnderEma200Service(database.Context, provider, NullLogger<UnderEma200Service>.Instance);

        var result = await service.RefreshAsync();

        var snapshots = await database.Context.InvestmentEma200Snapshots.ToListAsync();
        var mcd = Assert.Single(snapshots, snapshot => snapshot.Symbol == "MCD");
        var msft = Assert.Single(snapshots, snapshot => snapshot.Symbol == "MSFT");

        Assert.NotEmpty(result.Items);
        Assert.All(snapshots.Where(snapshot => snapshot.ErrorMessage is null), snapshot => Assert.NotEqual(0m, snapshot.Ema200Weekly));

        var mcdDaily = UnderEma200Calculator.EvaluateSnapshot(mcd, EmaTimeframe.Daily);
        var msftDaily = UnderEma200Calculator.EvaluateSnapshot(msft, EmaTimeframe.Daily);
        Assert.True(mcdDaily.IsBelowEma200);
        Assert.True(mcdDaily.IsQualified);
        Assert.Null(mcdDaily.RejectionReason);
        Assert.True(msftDaily.IsBelowEma200);
        Assert.False(msftDaily.IsQualified);
        Assert.Contains("35 % pod EMA200", msftDaily.RejectionReason);
    }

    [Fact]
    public async Task RefreshAsync_StoresOneSnapshotPerSymbol_WithDailyAndWeeklyEma()
    {
        await using var database = await CreateDatabaseAsync();
        var provider = new FakeHistoryProvider(new Dictionary<string, IReadOnlyList<UnderEma200PricePoint>>
        {
            ["MCD"] = BuildGrowthPullbackPrices(lastPriceFactor: 0.92m)
        });
        var service = new UnderEma200Service(database.Context, provider, NullLogger<UnderEma200Service>.Instance);

        await service.RefreshAsync(EmaTimeframe.Daily);
        await service.RefreshAsync(EmaTimeframe.Weekly);

        var snapshots = await database.Context.InvestmentEma200Snapshots.ToListAsync();
        var mcd = Assert.Single(snapshots, snapshot => snapshot.Symbol == "MCD");
        Assert.Equal(TradingJournal.Services.Screening.InvestmentUniverse.All.Count, snapshots.Count);
        Assert.True(mcd.Ema200Daily > 0);
        Assert.True(mcd.Ema200Weekly > 0);
    }

    [Fact]
    public async Task RefreshAsync_PreservesExistingSnapshot_WhenNoUsableHistoryIsDownloaded()
    {
        await using var database = await CreateDatabaseAsync();
        database.Context.InvestmentEma200Snapshots.Add(CreateExistingSnapshot());
        await database.Context.SaveChangesAsync();

        var service = new UnderEma200Service(
            database.Context,
            new FakeHistoryProvider(new Dictionary<string, IReadOnlyList<UnderEma200PricePoint>>()),
            NullLogger<UnderEma200Service>.Instance);

        var result = await service.RefreshAsync(EmaTimeframe.Daily);

        var snapshot = Assert.Single(await database.Context.InvestmentEma200Snapshots.ToListAsync());
        Assert.Equal("OLD", snapshot.Symbol);
        Assert.Single(result.Items);
        Assert.Contains("Puvodni snapshot zustal zachovany", result.Message);
    }

    [Fact]
    public async Task GetPersistedResultAsync_ReturnsStoredPageData()
    {
        await using var database = await CreateDatabaseAsync();
        database.Context.InvestmentEma200Snapshots.Add(CreateExistingSnapshot());
        await database.Context.SaveChangesAsync();

        var service = new UnderEma200Service(
            database.Context,
            new FakeHistoryProvider(new Dictionary<string, IReadOnlyList<UnderEma200PricePoint>>()),
            NullLogger<UnderEma200Service>.Instance);

        var result = await service.GetPersistedResultAsync(EmaTimeframe.Weekly);

        var item = Assert.Single(result.Items);
        Assert.Equal("OLD", item.Symbol);
        Assert.Equal("Weekly", item.Timeframe);
        Assert.True(item.IsBelowEma200);
        Assert.True(item.IsQualified);
    }

    [Fact]
    public async Task UnderEma200_ReturnsPersistedSnapshotModel()
    {
        await using var database = await CreateDatabaseAsync();
        database.Context.InvestmentEma200Snapshots.Add(CreateExistingSnapshot());
        await database.Context.SaveChangesAsync();

        var service = new UnderEma200Service(
            database.Context,
            new FakeHistoryProvider(new Dictionary<string, IReadOnlyList<UnderEma200PricePoint>>()),
            NullLogger<UnderEma200Service>.Instance);
        var controller = new InvestmentsController(
            service,
            new AppClock(),
            NullLogger<InvestmentsController>.Instance);

        var view = Assert.IsType<ViewResult>(await controller.UnderEma200("Daily", CancellationToken.None));
        var model = Assert.IsType<UnderEma200Result>(view.Model);
        var item = Assert.Single(model.Items);

        Assert.Equal("OLD", item.Symbol);
        Assert.True(item.IsBelowEma200);
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new SqliteApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new TestDatabase(connection, context);
    }

    private static InvestmentEma200Snapshot CreateExistingSnapshot(string symbol = "OLD")
    {
        return new InvestmentEma200Snapshot
        {
            Symbol = symbol,
            Name = "Old Snapshot",
            Sector = "Test",
            CurrentPrice = 90m,
            Ema200Daily = 100m,
            Ema200Weekly = 100m,
            Performance1YearPercent = 12m,
            Performance3YearPercent = 30m,
            Performance5YearPercent = 80m,
            DailyPriceCount = UnderEma200Calculator.FiveYearTradingDays,
            DailyEmaSourceCount = UnderEma200Calculator.EmaPeriod,
            WeeklyEmaSourceCount = UnderEma200Calculator.EmaPeriod,
            LastPriceDate = new DateTime(2026, 7, 20),
            CalculationDate = new DateTime(2026, 7, 20),
            UpdatedAt = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero),
            SnapshotCreatedAtUtc = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc),
            DataSource = "test"
        };
    }

    private static IReadOnlyList<UnderEma200PricePoint> BuildGrowthPullbackPrices(decimal lastPriceFactor)
    {
        const int count = UnderEma200Calculator.FiveYearTradingDays + UnderEma200Calculator.EmaPeriod + 40;
        var prices = new List<UnderEma200PricePoint>(count);
        var date = new DateTime(2020, 1, 1);

        for (var i = 0; i < count; i++)
        {
            prices.Add(new UnderEma200PricePoint(date.AddDays(i), 100m + (i * 0.08m)));
        }

        prices[^1] = prices[^1] with { Close = prices[^1].Close * lastPriceFactor };
        return prices;
    }

    private sealed class FakeHistoryProvider : IUnderEma200HistoryProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<UnderEma200PricePoint>> _pricesBySymbol;

        public FakeHistoryProvider(IReadOnlyDictionary<string, IReadOnlyList<UnderEma200PricePoint>> pricesBySymbol)
        {
            _pricesBySymbol = pricesBySymbol;
        }

        public Task<IReadOnlyList<UnderEma200PricePoint>> GetDailyPricesAsync(string symbol, CancellationToken cancellationToken)
        {
            return Task.FromResult(_pricesBySymbol.GetValueOrDefault(symbol, []));
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public TestDatabase(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ApplicationDbContext Context { get; }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SqliteApplicationDbContext : ApplicationDbContext
    {
        public SqliteApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            foreach (var property in builder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            {
                if (property.GetColumnType()?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true)
                {
                    property.SetColumnType("TEXT");
                }

                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetColumnType("REAL");
                }
            }
        }
    }
}
