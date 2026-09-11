using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services.Screening;

namespace TradingJournal.Services.Insiders;

public sealed class InsiderUpdateService(ApplicationDbContext db, IInsiderSecProvider provider, ILogger<InsiderUpdateService> logger)
{
    private static readonly DateOnly EarliestSupportedFiling = new(2026, 9, 1);
    public async Task<InsiderImportRun> RunAsync(DateOnly scheduledDate, CancellationToken ct, bool force = false)
    {
        var completed = await db.InsiderImportRuns.AsNoTracking().Where(x => x.CompletedAt != null)
            .OrderByDescending(x => x.ScheduledDate).FirstOrDefaultAsync(ct);
        if (!force && completed?.ScheduledDate >= scheduledDate) return completed;

        var hasData = await db.InsiderFilings.AnyAsync(ct);
        // Keep the import focused on current filings. Older SEC history is not
        // useful for the dashboard and can exhaust the provider/runtime quota.
        var since = hasData
            ? new[] { EarliestSupportedFiling, scheduledDate.AddDays(-14) }.Max()
            : EarliestSupportedFiling;
        var known = await db.InsiderFilings.AsNoTracking().Select(x => x.AccessionNumber).ToHashSetAsync(ct);
        var run = new InsiderImportRun { ScheduledDate = scheduledDate, StartedAt = DateTimeOffset.UtcNow };
        db.InsiderImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var errors = new List<string>();
        foreach (var stock in InvestmentUniverse.All)
        {
            ct.ThrowIfCancellationRequested();
            logger.LogInformation("Insiders {Index}/{Total}: {Ticker}", run.IssuersChecked + 1, InvestmentUniverse.All.Count, stock.Symbol);
            try
            {
                var recent = await provider.RecentFilingsAsync(stock, since, ct);
                var imported = new List<InsiderFiling>();
                foreach (var descriptor in recent.OrderBy(x => x.FiledDate))
                {
                    if (!known.Add(descriptor.AccessionNumber)) continue;
                    var filing = await provider.FilingAsync(stock, descriptor, ct);
                    db.InsiderFilings.Add(filing);
                    imported.Add(filing);
                }
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Insiders {Ticker}: uloženo {Filings} nových hlášení, {Transactions} transakcí.", stock.Symbol, imported.Count, imported.Sum(x => x.Transactions.Count));
                run.FilingsImported += imported.Count;
                run.TransactionsImported += imported.Sum(x => x.Transactions.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                run.IssuersFailed++;
                if (errors.Count < 12) errors.Add($"{stock.Symbol}: {ex.GetType().Name}");
                logger.LogWarning(ex, "Insider filings could not be refreshed for {Symbol}.", stock.Symbol);
                db.ChangeTracker.Clear();
                db.Attach(run);
            }
            finally { run.IssuersChecked++; }
            if (run.IssuersChecked % 10 == 0) await db.SaveChangesAsync(ct);
        }
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.ErrorSummary = errors.Count == 0 ? null : string.Join(", ", errors);
        await db.SaveChangesAsync(ct);
        return run;
    }
}

