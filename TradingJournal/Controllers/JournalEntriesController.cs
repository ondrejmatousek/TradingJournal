using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

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

        // GET: JournalEntries/TradeQuality
        public IActionResult TradeQuality()
        {
            return View();
        }

        // GET: JournalEntries/Strategy
        public IActionResult Strategy()
        {
            return View();
        }

        // GET: JournalEntries/Analysis
        public async Task<IActionResult> Analysis()
        {
            var userId = CurrentUserId;

            var entries = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .OrderBy(e => e.EntryTime ?? e.CreatedAt)
                .ToListAsync();

            var closedForWinRate = entries
                .Where(e => e.Outcome is TradeOutcome.Win or TradeOutcome.Loss)
                .ToList();

            var completedTrades = entries
                .Where(e => e.Outcome is TradeOutcome.Win or TradeOutcome.Loss or TradeOutcome.Breakeven)
                .ToList();

            var priceMoveTrades = entries
                .Select(e => new
                {
                    Entry = e,
                    PriceMove = CalculatePriceMove(e)
                })
                .Where(e => e.PriceMove.HasValue)
                .ToList();

            var runningMove = 0m;
            var equityCurve = priceMoveTrades
                .Select(e =>
                {
                    runningMove += e.PriceMove!.Value;
                    return new AnalysisPoint
                    {
                        Label = (e.Entry.EntryTime ?? e.Entry.CreatedAt).LocalDateTime.ToString("dd.MM."),
                        Value = runningMove
                    };
                })
                .ToList();

            var monthlyPerformance = priceMoveTrades
                .GroupBy(e => (e.Entry.EntryTime ?? e.Entry.CreatedAt).LocalDateTime.ToString("MMM yy"))
                .Select(g => new AnalysisBar
                {
                    Label = g.Key,
                    Value = g.Sum(e => e.PriceMove!.Value)
                })
                .ToList();

            var dailyPerformance = priceMoveTrades
                .GroupBy(e => (e.Entry.EntryTime ?? e.Entry.CreatedAt).LocalDateTime.ToString("ddd dd.MM."))
                .Select(g => new AnalysisBar
                {
                    Label = g.Key,
                    Value = g.Sum(e => e.PriceMove!.Value)
                })
                .TakeLast(10)
                .ToList();

            var assetBreakdown = entries
                .GroupBy(e => e.Asset)
                .Select(g =>
                {
                    var decisive = g.Where(e => e.Outcome is TradeOutcome.Win or TradeOutcome.Loss).ToList();
                    return new AssetBreakdown
                    {
                        Asset = g.Key,
                        Trades = g.Count(),
                        WinRate = decisive.Count == 0 ? 0 : decisive.Count(e => e.Outcome == TradeOutcome.Win) * 100m / decisive.Count,
                        NetPriceMove = g.Sum(e => CalculatePriceMove(e) ?? 0)
                    };
                })
                .OrderByDescending(x => x.Trades)
                .ThenBy(x => x.Asset)
                .ToList();

            var sideBreakdown = entries
                .GroupBy(e => e.Side)
                .Select(g =>
                {
                    var decisive = g.Where(e => e.Outcome is TradeOutcome.Win or TradeOutcome.Loss).ToList();
                    return new SideBreakdown
                    {
                        Side = g.Key,
                        Trades = g.Count(),
                        WinRate = decisive.Count == 0 ? 0 : decisive.Count(e => e.Outcome == TradeOutcome.Win) * 100m / decisive.Count,
                        NetPriceMove = g.Sum(e => CalculatePriceMove(e) ?? 0)
                    };
                })
                .OrderBy(x => x.Side)
                .ToList();

            var wins = entries.Count(e => e.Outcome == TradeOutcome.Win);
            var losses = entries.Count(e => e.Outcome == TradeOutcome.Loss);
            var breakeven = entries.Count(e => e.Outcome == TradeOutcome.Breakeven);
            var netPriceMove = priceMoveTrades.Sum(e => e.PriceMove!.Value);

            var model = new JournalAnalysisViewModel
            {
                Stats = new AnalysisStats
                {
                    TotalTrades = entries.Count,
                    ClosedTrades = completedTrades.Count,
                    OpenTrades = entries.Count(e => e.Status == EntryStatus.Open),
                    Wins = wins,
                    Losses = losses,
                    Breakeven = breakeven,
                    WinRate = closedForWinRate.Count == 0 ? 0 : wins * 100m / closedForWinRate.Count,
                    PlanAdherence = entries.Count == 0 ? 0 : entries.Count(e => e.FollowedTradingPlan) * 100m / entries.Count,
                    AverageRisk = entries.Count == 0 ? 0 : entries.Average(e => e.RiskPercentage),
                    NetPriceMove = netPriceMove,
                    AveragePriceMove = priceMoveTrades.Count == 0 ? 0 : netPriceMove / priceMoveTrades.Count
                },
                EquityCurve = equityCurve,
                MonthlyPerformance = monthlyPerformance,
                DailyPerformance = dailyPerformance,
                AssetBreakdown = assetBreakdown,
                SideBreakdown = sideBreakdown,
                RecentTrades = entries
                    .OrderByDescending(e => e.EntryTime ?? e.CreatedAt)
                    .Take(8)
                    .Select(e => new RecentTradeRow
                    {
                        Id = e.Id,
                        Date = (e.EntryTime ?? e.CreatedAt).LocalDateTime.ToString("dd.MM.yyyy"),
                        Asset = e.Asset,
                        Side = e.Side,
                        Outcome = e.Outcome,
                        Status = e.Status,
                        PriceMove = CalculatePriceMove(e)
                    })
                    .ToList()
            };

            return View(model);
        }

        private static decimal? CalculatePriceMove(JournalEntry entry)
        {
            if (!entry.ExitPrice.HasValue || entry.EntryPrice == 0)
                return null;

            var entryPrice = NormalizePriceForAnalysis(entry.Asset, entry.EntryPrice);
            var exitPrice = NormalizePriceForAnalysis(entry.Asset, entry.ExitPrice.Value);
            var rawMove = exitPrice - entryPrice;
            return entry.Side == PositionSide.Long ? rawMove : -rawMove;
        }

        private static decimal NormalizePriceForAnalysis(AssetSymbol asset, decimal price)
        {
            if (!IsForexAsset(asset) || price <= 10m)
                return price;

            while (price > 10m)
                price /= 10m;

            return price;
        }

        private static bool IsForexAsset(AssetSymbol asset)
        {
            return asset is AssetSymbol.EURUSD or AssetSymbol.GBPUSD;
        }

        // GET: JournalEntries
        public async Task<IActionResult> Index(
            AssetSymbol? asset,
            TradeOutcome? outcome,
            PositionSide? side,
            bool planOnly = false)
        {
            var userId = CurrentUserId;

            var allUserEntries = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Images)
                .Where(e => e.UserId == userId)
                .ToListAsync();

            var filteredEntries = allUserEntries.AsEnumerable();

            if (asset.HasValue)
                filteredEntries = filteredEntries.Where(e => e.Asset == asset.Value);

            if (outcome.HasValue)
                filteredEntries = filteredEntries.Where(e => e.Outcome == outcome.Value);

            if (side.HasValue)
                filteredEntries = filteredEntries.Where(e => e.Side == side.Value);

            if (planOnly)
                filteredEntries = filteredEntries.Where(e => e.FollowedTradingPlan);

            var entries = filteredEntries
                .OrderByDescending(e => e.EntryTime ?? e.CreatedAt)
                .ToList();

            var completedTrades = entries
                .Where(e => e.Outcome is TradeOutcome.Win or TradeOutcome.Loss or TradeOutcome.Breakeven)
                .ToList();

            var wins = entries.Count(e => e.Outcome == TradeOutcome.Win);
            var losses = entries.Count(e => e.Outcome == TradeOutcome.Loss);
            var breakeven = entries.Count(e => e.Outcome == TradeOutcome.Breakeven);
            var unknown = entries.Count(e => e.Outcome == TradeOutcome.Unknown);

            var bestAsset = entries
                .Where(e => e.Outcome is TradeOutcome.Win or TradeOutcome.Loss)
                .GroupBy(e => e.Asset)
                .Select(g => new
                {
                    Asset = g.Key,
                    WinRate = g.Count(e => e.Outcome == TradeOutcome.Win) * 100m / g.Count()
                })
                .OrderByDescending(g => g.WinRate)
                .ThenBy(g => g.Asset)
                .FirstOrDefault();

            var model = new JournalIndexViewModel
            {
                Entries = entries,
                Filters = new JournalIndexFilters
                {
                    Asset = asset,
                    Outcome = outcome,
                    Side = side,
                    PlanOnly = planOnly
                },
                Stats = new JournalStats
                {
                    TotalTrades = entries.Count,
                    Wins = wins,
                    Losses = losses,
                    Breakeven = breakeven,
                    Unknown = unknown,
                    WinRate = completedTrades.Count == 0 ? 0 : wins * 100m / completedTrades.Count,
                    PlanAdherence = entries.Count == 0 ? 0 : entries.Count(e => e.FollowedTradingPlan) * 100m / entries.Count,
                    AverageRisk = entries.Count == 0 ? 0 : entries.Average(e => e.RiskPercentage),
                    BestAsset = bestAsset?.Asset,
                    BestAssetWinRate = bestAsset?.WinRate
                },
                AssetOptions = Enum.GetValues<AssetSymbol>()
                    .Select(value => new SelectListItem(value.ToString(), value.ToString(), value == asset))
                    .ToList(),
                OutcomeOptions = Enum.GetValues<TradeOutcome>()
                    .Select(value => new SelectListItem(value.ToString(), value.ToString(), value == outcome))
                    .ToList(),
                SideOptions = Enum.GetValues<PositionSide>()
                    .Select(value => new SelectListItem(value.ToString(), value.ToString(), value == side))
                    .ToList()
            };

            return View(model);
        }

        // GET: JournalEntries/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Images)
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

        // GET: JournalEntries/GalleryImage/5
        [HttpGet]
        public async Task<IActionResult> GalleryImage(int id)
        {
            var userId = CurrentUserId;

            var image = await _context.JournalEntryImages
                .AsNoTracking()
                .Where(i => i.Id == id && i.JournalEntry.UserId == userId)
                .Select(i => new { i.ImageData, i.ImageContentType })
                .FirstOrDefaultAsync();

            if (image?.ImageData == null || string.IsNullOrWhiteSpace(image.ImageContentType))
                return NotFound();

            return File(image.ImageData, image.ImageContentType);
        }

        // GET: JournalEntries/Create
        public IActionResult Create()
        {
            return View(new JournalEntry
            {
                EntryTime = DateTimeOffset.Now,
                Status = EntryStatus.Open
            });
        }

        // POST: JournalEntries/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JournalEntry entry, IReadOnlyList<IFormFile>? imageFiles, IFormFile? imageFile)
        {
            ApplyDecimalFormValues(entry);

            // Server-side vlastnictví + created timestamp
            entry.UserId = CurrentUserId;
            entry.CreatedAt = DateTimeOffset.UtcNow;
            NormalizeOpenTrade(entry);

            // Upload obrázku do DB
            await ApplyImageUploadsAsync(entry, imageFiles, imageFile);

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
                .Include(e => e.Images)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            return View(entry);
        }

        // POST: JournalEntries/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, JournalEntry form, IReadOnlyList<IFormFile>? imageFiles, IFormFile? imageFile, bool removeImage = false)
        {
            ApplyDecimalFormValues(form);

            var userId = CurrentUserId;

            // Načteme původní záznam patřící uživateli
            var entry = await _context.JournalEntries
                .Include(e => e.Images)
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
            entry.Status = form.Status;
            NormalizeOpenTrade(entry);

            // Smazání obrázku (volitelné)
            if (removeImage)
            {
                entry.ImageData = null;
                entry.ImageContentType = null;
                entry.ImageFileName = null;
                _context.JournalEntryImages.RemoveRange(entry.Images);
            }

            // Nové uploady se přidají do galerie.
            await ApplyImageUploadsAsync(entry, imageFiles, imageFile);

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
                .Include(e => e.Images)
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
                .Include(e => e.Images)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);

            if (entry == null) return NotFound();

            _context.JournalEntries.Remove(entry);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private static void NormalizeOpenTrade(JournalEntry entry)
        {
            if (entry.Status != EntryStatus.Open)
                return;

            entry.ExitTime = null;
            entry.ExitPrice = null;
            entry.Outcome = TradeOutcome.Unknown;
        }

        private void ApplyDecimalFormValues(JournalEntry entry)
        {
            if (TryReadDecimalFromForm(nameof(JournalEntry.RiskPercentage), out var riskPercentage) && riskPercentage.HasValue)
                entry.RiskPercentage = riskPercentage.Value;

            if (TryReadDecimalFromForm(nameof(JournalEntry.EntryPrice), out var entryPrice) && entryPrice.HasValue)
                entry.EntryPrice = entryPrice.Value;

            if (TryReadDecimalFromForm(nameof(JournalEntry.ExitPrice), out var exitPrice))
                entry.ExitPrice = exitPrice;
        }

        private bool TryReadDecimalFromForm(string key, out decimal? value)
        {
            value = null;

            if (!Request.HasFormContentType || !Request.Form.TryGetValue(key, out var formValue))
                return false;

            var rawValue = formValue.ToString().Trim();
            if (string.IsNullOrWhiteSpace(rawValue))
                return true;

            var normalized = NormalizeDecimalInput(rawValue);
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static string NormalizeDecimalInput(string value)
        {
            var normalized = value.Trim().Replace(" ", string.Empty);
            var lastComma = normalized.LastIndexOf(',');
            var lastDot = normalized.LastIndexOf('.');

            if (lastComma >= 0 && lastDot >= 0)
            {
                var decimalSeparator = lastComma > lastDot ? ',' : '.';
                var thousandsSeparator = decimalSeparator == ',' ? "." : ",";
                normalized = normalized.Replace(thousandsSeparator, string.Empty);
                return normalized.Replace(decimalSeparator, '.');
            }

            return normalized.Replace(',', '.');
        }

        private async Task ApplyImageUploadsAsync(JournalEntry entry, IReadOnlyList<IFormFile>? imageFiles, IFormFile? legacyImageFile)
        {
            var files = new List<IFormFile>();

            if (imageFiles is not null)
                files.AddRange(imageFiles.Where(file => file.Length > 0));

            if (legacyImageFile is { Length: > 0 })
                files.Add(legacyImageFile);

            if (files.Count == 0)
                return;

            if (files.Count > 8)
            {
                ModelState.AddModelError("", "Nahraj maximálně 8 obrázků k jednomu obchodu.");
                return;
            }

            const long maxBytes = 5 * 1024 * 1024; // 5 MB
            foreach (var imageFile in files)
            {
                if (imageFile.Length > maxBytes)
                {
                    ModelState.AddModelError("", $"Obrázek {imageFile.FileName} je příliš velký (max 5 MB).");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(imageFile.ContentType) || !imageFile.ContentType.StartsWith("image/"))
                {
                    ModelState.AddModelError("", $"Soubor {imageFile.FileName} musí být obrázek.");
                    continue;
                }

                using var ms = new MemoryStream();
                await imageFile.CopyToAsync(ms);

                entry.Images.Add(new JournalEntryImage
                {
                    ImageData = ms.ToArray(),
                    ImageContentType = imageFile.ContentType,
                    ImageFileName = Path.GetFileName(imageFile.FileName),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }
    }
}
