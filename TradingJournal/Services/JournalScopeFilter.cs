using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;

namespace TradingJournal.Services;

public sealed class JournalScopeFilter(ApplicationDbContext db) : IAsyncActionFilter
{
    private static readonly HashSet<string> ScopedActions = new(StringComparer.OrdinalIgnoreCase)
    { "Index", "Analysis", nameof(Controllers.JournalEntriesController.ExportSession), "Details", "Create", "Edit", "Delete", "DeleteConfirmed", "Image", "GalleryImage", "DeleteGalleryImage", "DeleteLegacyImage" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor.RouteValues["action"] ?? "";
        if (!ScopedActions.Contains(action)) { await next(); return; }
        var user = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var value = context.RouteData.Values["journalId"]?.ToString() ?? context.HttpContext.Request.Query["journalId"].FirstOrDefault();
        if (!int.TryParse(value, out var journalId))
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method)) { context.Result = new BadRequestResult(); return; }
            // Preserve existing bookmarks for individual entries and images.
            if (int.TryParse(context.RouteData.Values["id"]?.ToString(), out var entryId))
            {
                int? legacyJournal = action == "GalleryImage"
                    ? await db.JournalEntryImages.Where(i => i.Id == entryId && i.JournalEntry.UserId == user).Select(i => (int?)i.JournalEntry.JournalId).SingleOrDefaultAsync()
                    : await db.JournalEntries.Where(e => e.Id == entryId && e.UserId == user).Select(e => (int?)e.JournalId).SingleOrDefaultAsync();
                context.Result = legacyJournal.HasValue
                    ? new RedirectToRouteResult("journal", new { journalId = legacyJournal.Value, action, id = entryId })
                    : new NotFoundResult();
                return;
            }
            context.Result = new RedirectToActionResult("Index", "TradingJournals", null);
            return;
        }
        var journal = await db.TradingJournals.AsNoTracking().SingleOrDefaultAsync(j => j.Id == journalId && j.UserId == user);
        if (journal is null) { context.Result = new NotFoundResult(); return; }
        // The path is authoritative. Form data cannot select or move an entry to another journal.
        ((Controller)context.Controller).ViewData["CurrentJournal"] = journal;
        context.RouteData.Values["journalId"] = journalId;
        await next();
    }
}
