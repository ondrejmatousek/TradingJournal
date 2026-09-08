using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services.Insiders;
using TradingJournal.Services.Screening;
using TradingJournal.ViewModels;

namespace TradingJournal.Controllers;

[Authorize]
public sealed class InsidersController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? sector, string? role, decimal? minimumValue,
        bool clustersOnly = false, DateOnly? from = null, DateOnly? to = null, int page = 1, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var selectedFrom = from ?? today.AddDays(-90);
        var selectedTo = to ?? today;
        if (selectedFrom > selectedTo) (selectedFrom, selectedTo) = (selectedTo, selectedFrom);
        page = Math.Max(1, page);
        const int pageSize = 50;
        var contextFrom = new[] { selectedFrom, today.AddDays(-180) }.Min();
        var transactions = await db.InsiderTransactions.AsNoTracking()
            .Where(x => !x.IsDerivative && x.TransactionCode == "P" && x.AcquisitionDisposition == "A"
                && x.Shares > 0 && x.PricePerShare > 0 && x.TransactionDate >= contextFrom && x.TransactionDate <= selectedTo)
            .Include(x => x.Filing).ThenInclude(x => x.ReportingOwners).ToListAsync(ct);
        var universe = InvestmentUniverse.All.ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
        var rows = transactions.Select(transaction => BuildRow(transaction, transactions, universe)).Where(x => x != null).Cast<InsiderRowViewModel>().ToList();
        var recent = rows.Where(x => x.TransactionDate >= today.AddDays(-30)).ToArray();
        var filtered = rows.Where(x => x.TransactionDate >= selectedFrom && x.TransactionDate <= selectedTo);
        if (!string.IsNullOrWhiteSpace(search)) filtered = filtered.Where(x => x.Symbol.Contains(search, StringComparison.OrdinalIgnoreCase) || x.CompanyName.Contains(search, StringComparison.OrdinalIgnoreCase) || x.InsiderName.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(sector)) filtered = filtered.Where(x => x.Sector == sector);
        if (!string.IsNullOrWhiteSpace(role)) filtered = filtered.Where(x => x.Role.Contains(role, StringComparison.OrdinalIgnoreCase));
        if (minimumValue != null) filtered = filtered.Where(x => x.Value >= minimumValue);
        if (clustersOnly) filtered = filtered.Where(x => x.ClusterInsiders >= 2);
        var ordered = filtered.OrderByDescending(x => x.Score).ThenByDescending(x => x.Value).ThenByDescending(x => x.TransactionDate).ToArray();
        var totalPages = Math.Max(1, (int)Math.Ceiling(ordered.Length / (decimal)pageSize));
        page = Math.Min(page, totalPages);
        var lastRun = await db.InsiderImportRuns.AsNoTracking().OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(ct);
        var model = new InsidersViewModel
        {
            Search = search, Sector = sector, Role = role, MinimumValue = minimumValue, ClustersOnly = clustersOnly,
            From = selectedFrom, To = selectedTo, Page = page, PageSize = pageSize, TotalRows = ordered.Length,
            TotalPages = totalPages,
            Sectors = InvestmentUniverse.All.Select(x => x.Sector).Distinct().Order().ToArray(),
            Rows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), LastUpdatedAt = lastRun?.CompletedAt,
            IssuersFailed = lastRun?.IssuersFailed ?? 0, Purchases30Days = recent.Length,
            Value30Days = recent.Sum(x => x.Value), ClusterSignals30Days = recent.Count(x => x.ClusterInsiders >= 2)
        };
        return View(model);
    }

    private static InsiderRowViewModel? BuildRow(InsiderTransaction transaction, IReadOnlyCollection<InsiderTransaction> all,
        IReadOnlyDictionary<string, InvestmentSymbol> universe)
    {
        if (!universe.TryGetValue(transaction.Filing.Symbol, out var stock) || transaction.PricePerShare == null) return null;
        var owners = transaction.Filing.ReportingOwners;
        var ownerKeys = owners.Select(x => x.OwnerCik ?? x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var related = all.Where(x => x.Filing.Symbol == transaction.Filing.Symbol && x.TransactionDate <= transaction.TransactionDate
            && x.TransactionDate >= transaction.TransactionDate.AddDays(-30)).ToArray();
        var cluster = related.SelectMany(x => x.Filing.ReportingOwners).Select(x => x.OwnerCik ?? x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var repeatDays = all.Where(x => x.Filing.Symbol == transaction.Filing.Symbol && x.TransactionDate >= transaction.TransactionDate.AddDays(-180)
            && x.TransactionDate <= transaction.TransactionDate && x.Filing.ReportingOwners.Any(o => ownerKeys.Contains(o.OwnerCik ?? o.Name)))
            .Select(x => x.TransactionDate).Distinct().Count();
        var prior = transaction.SharesOwnedAfter - transaction.Shares;
        decimal? increase = prior > 0 ? transaction.Shares / prior * 100 : transaction.SharesOwnedAfter == transaction.Shares ? 100 : null;
        var value = transaction.Shares * transaction.PricePerShare.Value;
        var signal = InsiderSignalCalculator.Calculate(owners, value, increase, cluster, repeatDays);
        return new InsiderRowViewModel
        {
            TransactionDate = transaction.TransactionDate, FiledDate = transaction.Filing.FiledDate,
            FilingDelayDays = Math.Max(0, transaction.Filing.FiledDate.DayNumber - transaction.TransactionDate.DayNumber),
            Symbol = stock.Symbol, CompanyName = transaction.Filing.IssuerName, Sector = stock.Sector,
            InsiderName = string.Join(", ", owners.Select(x => x.Name).Distinct()), Role = InsiderSignalCalculator.RoleLabel(owners),
            Shares = transaction.Shares, PricePerShare = transaction.PricePerShare.Value, Value = value,
            OwnershipIncreasePercent = increase, ClusterInsiders = cluster, RepeatPurchaseDays = repeatDays,
            Score = signal.Score, ScoreCoverage = signal.Coverage, ScoreExplanation = signal.Explanation,
            IsAmendment = transaction.Filing.IsAmendment, IsTenB5One = transaction.IsTenB5One, SourceUrl = transaction.Filing.SourceUrl
        };
    }
}
