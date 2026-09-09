using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services.Insiders;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public sealed class InsiderUpdateServiceTests
{
    [Fact]
    public async Task DailyImport_PersistsFilingOnce_AndSkipsCompletedDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new SqliteContext(options);
        await db.Database.EnsureCreatedAsync();
        var provider = new FakeProvider();
        var service = new InsiderUpdateService(db, provider, NullLogger<InsiderUpdateService>.Instance);
        var date = new DateOnly(2026, 9, 8);

        var first = await service.RunAsync(date, CancellationToken.None);
        var callsAfterFirst = provider.SubmissionCalls;
        var second = await service.RunAsync(date, CancellationToken.None);

        Assert.Equal(1, first.FilingsImported);
        Assert.Equal(1, await db.InsiderFilings.CountAsync());
        Assert.Equal(1, await db.InsiderTransactions.CountAsync());
        Assert.Equal(callsAfterFirst, provider.SubmissionCalls);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(new DateOnly(2026, 9, 1), provider.LastSince);
    }

    private sealed class FakeProvider : IInsiderSecProvider
    {
        public int SubmissionCalls { get; private set; }
        public DateOnly LastSince { get; private set; }
        public Task<IReadOnlyList<SecOwnershipFiling>> RecentFilingsAsync(InvestmentSymbol stock, DateOnly since, CancellationToken ct)
        {
            SubmissionCalls++;
            LastSince = since;
            IReadOnlyList<SecOwnershipFiling> result = stock.Symbol == "AAPL"
                ? [new("0000320193-26-000001", new(2026, 9, 7), new(2026, 9, 6), "4", "form4.xml", "Apple Inc.", 320193)] : [];
            return Task.FromResult(result);
        }

        public Task<InsiderFiling> FilingAsync(InvestmentSymbol stock, SecOwnershipFiling item, CancellationToken ct)
            => Task.FromResult(new InsiderFiling
            {
                AccessionNumber = item.AccessionNumber, IssuerCik = item.Cik.ToString(), Symbol = stock.Symbol,
                IssuerName = item.CompanyName, FormType = item.FormType, FiledDate = item.FiledDate,
                SourceUrl = "https://www.sec.gov/example", ImportedAt = DateTimeOffset.UtcNow,
                ReportingOwners = [new() { Name = "Test CEO", IsOfficer = true, OfficerTitle = "CEO" }],
                Transactions = [new() { SourceIndex = 0, TransactionDate = new(2026, 9, 6), SecurityTitle = "Common Stock", TransactionCode = "P", AcquisitionDisposition = "A", Shares = 100, PricePerShare = 200 }]
            });
    }

    private sealed class SqliteContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
            {
                if (property.GetColumnType()?.Contains("(max)") == true) property.SetColumnType("TEXT");
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)) property.SetColumnType("REAL");
            }
        }
    }
}
