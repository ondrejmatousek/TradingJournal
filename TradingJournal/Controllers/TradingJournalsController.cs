using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;

namespace TradingJournal.Controllers;

[Authorize]
public class TradingJournalsController(ApplicationDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index()
    {
        var journals = await db.TradingJournals.AsNoTracking().Where(j => j.UserId == UserId)
            .OrderBy(j => j.Name).Select(j => new JournalListItem(j.Id, j.Name, j.Description, j.Entries.Count)).ToListAsync();
        return View(journals);
    }

    public IActionResult Create() => View("Form", new TradingJournalBook());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TradingJournalBook form)
    {
        Normalize(form);
        if (!ModelState.IsValid) return View("Form", form);
        var journal = new TradingJournalBook { Name = form.Name, Description = form.Description, UserId = UserId };
        db.TradingJournals.Add(journal);
        await db.SaveChangesAsync();
        return RedirectToRoute("journal", new { journalId = journal.Id, action = "Index" });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var journal = await db.TradingJournals.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.UserId == UserId);
        return journal is null ? NotFound() : View("Form", journal);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TradingJournalBook form)
    {
        var journal = await db.TradingJournals.SingleOrDefaultAsync(j => j.Id == id && j.UserId == UserId);
        if (journal is null) return NotFound();
        Normalize(form);
        if (!ModelState.IsValid) { form.Id = id; return View("Form", form); }
        journal.Name = form.Name;
        journal.Description = form.Description;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var journal = await db.TradingJournals.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.UserId == UserId);
        if (journal is null) return NotFound();
        ViewData["EntryCount"] = await db.JournalEntries.CountAsync(e => e.JournalId == id);
        return View(journal);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var journal = await db.TradingJournals.SingleOrDefaultAsync(j => j.Id == id && j.UserId == UserId);
        if (journal is null) return NotFound();
        if (await db.JournalEntries.AnyAsync(e => e.JournalId == id))
        {
            ModelState.AddModelError("", "Journal obsahuje obchody. Před smazáním journalu je nutné odstranit jeho obchody.");
            ViewData["EntryCount"] = await db.JournalEntries.CountAsync(e => e.JournalId == id);
            return View("Delete", journal);
        }
        db.TradingJournals.Remove(journal);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            if (!await db.JournalEntries.AsNoTracking().AnyAsync(e => e.JournalId == id)) throw;
            // A trade may have been added between the check and DELETE. The FK prevents loss.
            db.Entry(journal).State = EntityState.Unchanged;
            ModelState.AddModelError("", "Do journalu byl přidán obchod. Journal nebyl smazán.");
            ViewData["EntryCount"] = await db.JournalEntries.CountAsync(e => e.JournalId == id);
            return View("Delete", journal);
        }
        return RedirectToAction(nameof(Index));
    }

    private void Normalize(TradingJournalBook form)
    {
        form.Name = (form.Name ?? "").Trim();
        form.Description = form.Description?.Trim();
        if (form.Name.Length == 0) ModelState.AddModelError(nameof(form.Name), "Zadejte název journalu.");
    }
}

public sealed record JournalListItem(int Id, string Name, string? Description, int EntryCount);
