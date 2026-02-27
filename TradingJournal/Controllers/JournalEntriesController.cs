using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradingJournal.Data;
using TradingJournal.Models;

namespace TradingJournal.Controllers
{
    [Authorize]
    public class JournalEntriesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public JournalEntriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        private string CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: JournalEntries
        public async Task<IActionResult> Index()
        {
            var userId = CurrentUserId;

            var entries = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            return View(entries);
        }

        // GET: JournalEntries/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            return View(entry);
        }

        // GET: JournalEntries/Image/5
        [HttpGet]
        public async Task<IActionResult> Image(int id)
        {
            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.Id == id && e.UserId == userId)
                .Select(e => new { e.ImageData, e.ImageContentType })
                .FirstOrDefaultAsync();

            if (entry?.ImageData == null || string.IsNullOrWhiteSpace(entry.ImageContentType))
                return NotFound();

            return File(entry.ImageData, entry.ImageContentType);
        }

        // GET: JournalEntries/Create
        public IActionResult Create()
        {
            return View(new JournalEntry
            {
                EntryTime = DateTimeOffset.Now
            });
        }

        // POST: JournalEntries/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JournalEntry entry, IFormFile? imageFile)
        {
            // Server-side vlastnictví + created timestamp
            entry.UserId = CurrentUserId;
            entry.CreatedAt = DateTimeOffset.UtcNow;

            // Upload obrázku do DB
            await ApplyImageUploadAsync(entry, imageFile);

            if (!ModelState.IsValid)
                return View(entry);

            _context.Add(entry);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: JournalEntries/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            return View(entry);
        }

        // POST: JournalEntries/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, JournalEntry form, IFormFile? imageFile, bool removeImage = false)
        {
            var userId = CurrentUserId;

            // Načteme původní záznam patřící uživateli
            var entry = await _context.JournalEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            // Aktualizujeme jen povolená pole (ochrana proti overposting)
            entry.Asset = form.Asset;
            entry.Side = form.Side;
            entry.EntryTime = form.EntryTime;
            entry.ExitTime = form.ExitTime;
            entry.Note = form.Note;
            entry.FeelingsBeforeEntry = form.FeelingsBeforeEntry;
            entry.Outcome = form.Outcome;
            entry.RiskPercentage = form.RiskPercentage;
            entry.EntryPrice = form.EntryPrice;
            entry.ExitPrice = form.ExitPrice;
            entry.FollowedTradingPlan = form.FollowedTradingPlan;

            // Smazání obrázku (volitelné)
            if (removeImage)
            {
                entry.ImageData = null;
                entry.ImageContentType = null;
                entry.ImageFileName = null;
            }

            // Nový upload obrázku (přepíše starý)
            await ApplyImageUploadAsync(entry, imageFile);

            if (!ModelState.IsValid)
                return View(entry);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // GET: JournalEntries/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            return View(entry);
        }

        // POST: JournalEntries/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            _context.JournalEntries.Remove(entry);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task ApplyImageUploadAsync(JournalEntry entry, IFormFile? imageFile)
        {
            if (imageFile is not { Length: > 0 })
                return;

            // jednoduché omezení – uprav dle potřeby
            const long maxBytes = 5 * 1024 * 1024; // 5 MB
            if (imageFile.Length > maxBytes)
            {
                ModelState.AddModelError("", "Obrázek je příliš velký (max 5 MB).");
                return;
            }

            // základní kontrola typu
            if (string.IsNullOrWhiteSpace(imageFile.ContentType) || !imageFile.ContentType.StartsWith("image/"))
            {
                ModelState.AddModelError("", "Soubor musí být obrázek.");
                return;
            }

            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);

            entry.ImageData = ms.ToArray();
            entry.ImageContentType = imageFile.ContentType;
            entry.ImageFileName = Path.GetFileName(imageFile.FileName);
        }
    }
}