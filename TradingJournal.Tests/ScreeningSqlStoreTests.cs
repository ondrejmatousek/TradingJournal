using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using TradingJournal.Data;
using TradingJournal.Services.Screening;
using TradingJournal.Models;

namespace TradingJournal.Tests;

public sealed class ScreeningSqlStoreTests
{
    [Fact]
    public async Task SeparateWebAndWorkerInstances_ShareDurableSqlValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var web = fixture.Store("web");
        await fixture.Store("worker").WriteAsync("market-MSFT", new[] { 120m, 130m }, default);
        Assert.Equal(new[] { 120m, 130m }, await web.ReadAsync<decimal[]>("market-MSFT", default));
        await web.WriteAsync("market-MSFT", new[] { 140m }, default);
        Assert.Equal(new[] { 140m }, await fixture.Store("restarted-worker").ReadAsync<decimal[]>("market-MSFT", default));
        using var scope = fixture.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScreeningDocuments.ToListAsync();
        Assert.Single(rows);
        Assert.Equal(64, rows[0].Id.Length);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "worker", "App_Data")));
    }

    [Fact]
    public async Task LegacyFile_IsImportedOnceAndCannotOverwriteNewSqlData()
    {
        await using var fixture = await Fixture.CreateAsync();
        var legacy = new ScreeningStore(fixture.Environment("web"));
        await legacy.WriteAsync("research-user-MSFT", new[] { "original research" }, default);
        var web = fixture.Store("web");
        Assert.Equal(new[] { "original research" }, await web.ReadAsync<string[]>("research-user-MSFT", default));
        await fixture.Store("worker").WriteAsync("research-user-MSFT", new[] { "new research" }, default);
        Assert.Equal(new[] { "new research" }, await web.ReadAsync<string[]>("research-user-MSFT", default));
        Assert.Equal(new[] { "original research" }, await legacy.ReadAsync<string[]>("research-user-MSFT", default));
    }

    [Fact]
    public async Task SqlFailure_DoesNotSilentlySwitchBackToStaleFiles()
    {
        await using var fixture = await Fixture.CreateAsync();
        await new ScreeningStore(fixture.Environment("web")).WriteAsync("macro-v1", new[] { 1 }, default);
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE ScreeningDocuments");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => fixture.Store("web").ReadAsync<int[]>("macro-v1", default));
    }

    [Fact]
    public async Task PersonalResearchRemainsSeparateAndJobStateSurvivesRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var web = fixture.Store("web");
        await web.WriteAsync("research-user-a-MSFT", new[] { "A" }, default);
        await web.WriteAsync("research-user-b-MSFT", new[] { "B" }, default);
        var now = DateTimeOffset.UtcNow;
        var run = new ScreeningUpdateRun(new(2026, 9, 7), now, now, 200, 12, true);
        await fixture.Store("worker").WriteAsync(ScreeningUpdateHostedService.StateKey, run, default);
        Assert.Equal(new[] { "A" }, await web.ReadAsync<string[]>("research-user-a-MSFT", default));
        Assert.Equal(new[] { "B" }, await web.ReadAsync<string[]>("research-user-b-MSFT", default));
        Assert.Equal(run, await fixture.Store("restarted").ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, default));
    }

    [Fact]
    public async Task ValuationHistory_IsUpsertedBySymbolAndDateAndHasJsonFallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        var valuation = ScreeningCalculator.Value(new(new(2025, 12, 31), new(2026, 2, 15), 1100, 1000, 250, 50, 150, 1500, 500, 100, "https://sec.gov"), "Technology")!;
        var row = new ScreeningRow { Symbol = "MSFT", Price = 100, Valuation = ScreeningCalculator.Reprice(valuation, 100) };
        await fixture.Store("worker").WriteValuationAsync("MSFT", new(2026, 9, 10), row, default);
        var updatedRow = new ScreeningRow { Symbol = "MSFT", Price = 101, Valuation = ScreeningCalculator.Reprice(valuation, 101) };
        await fixture.Store("worker").WriteValuationAsync("MSFT", new(2026, 9, 10), updatedRow, default);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entities = await db.CompanyValuations.Where(x => x.Symbol == "MSFT").ToListAsync();
        Assert.Single(entities);
        Assert.Equal(101, entities[0].CurrentPrice);
        Assert.Contains("dcfFairValue", entities[0].MethodsJson, StringComparison.OrdinalIgnoreCase);
        var history = await fixture.Store("reader").ReadAsync<ValuationResult>("valuation-history-MSFT-20260910", default);
        Assert.NotNull(history);
        Assert.Equal(valuation.Base, history!.Base);
    }

    [Fact]
    public async Task ConcurrentInstances_UpsertOneDocument()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Task.WhenAll(fixture.Store("web").WriteAsync("market-MSFT", 1, default), fixture.Store("worker").WriteAsync("market-MSFT", 2, default));
        using var scope = fixture.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ScreeningDocuments.CountAsync());
        Assert.Contains(await fixture.Store("reader").ReadAsync<int>("market-MSFT", default), new[] { 1, 2 });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "screening-sql-tests", Guid.NewGuid().ToString("N"));
        public ServiceProvider Services { get; private set; } = null!;
        public IWebHostEnvironment Environment(string name) => new TestEnvironment { ContentRootPath = Path.Combine(Root, name) };
        public ScreeningStore Store(string name) => new(Environment(name), Services.GetRequiredService<IServiceScopeFactory>());
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.Root);
            fixture.Services = new ServiceCollection().AddScoped<ApplicationDbContext>(_ => new SqliteContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={Path.Combine(fixture.Root, "store.db")};Pooling=False").Options)).BuildServiceProvider();
            using var scope = fixture.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            return fixture;
        }
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "screening-sql-tests")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Root);
            if (target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(target, true);
        }
    }
    private sealed class SqliteContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property.GetColumnType()?.Contains("(max)") == true) property.SetColumnType("TEXT");
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)) property.SetColumnType("REAL");
            }
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
