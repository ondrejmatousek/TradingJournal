using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services;
using TradingJournal.ViewModels;
using System.Text.Json;

namespace TradingJournal.Controllers
{
    [Authorize]
    [ServiceFilter(typeof(JournalScopeFilter))]
    public class JournalEntriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly MuckaRubricProvider _muckaRubricProvider;
        private static readonly HashSet<string> StrategyNoteKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "mucka",
            "corex"
        };

        private static readonly HashSet<string> SetupReviewOutcomes = new(StringComparer.OrdinalIgnoreCase)
        {
            "1",
            "2",
            "3"
        };

        public JournalEntriesController(
            ApplicationDbContext context,
            MuckaRubricProvider muckaRubricProvider)
        {
            _context = context;
            _muckaRubricProvider = muckaRubricProvider;
        }

        private string CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        private int CurrentJournalId => ((TradingJournalBook)ViewData["CurrentJournal"]!).Id;

        // GET: JournalEntries/TradeQuality
        public IActionResult TradeQuality()
        {
            return View();
        }

        // GET: JournalEntries/SetupReview
        public async Task<IActionResult> SetupReview()
        {
            var userId = CurrentUserId;
            var reviews = await _context.TradeSetupReviews
                .AsNoTracking()
                .Include(r => r.Images)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.TradeDate)
                .ThenByDescending(r => r.TradeTimeText)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(BuildSetupReviewViewModel(reviews));
        }

        // POST: JournalEntries/SetupReview
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetupReview(TradeSetupReview form, IReadOnlyList<IFormFile>? imageFiles)
        {
            var userId = CurrentUserId;
            var now = DateTimeOffset.UtcNow;

            form.UserId = userId;
            form.CreatedAt = now;
            form.UpdatedAt = now;
            form.StrategyKey = NormalizeSetupReviewText(form.StrategyKey, "mucka", 64) ?? "mucka";
            form.TradeTimeText = NormalizeSetupReviewTime(form.TradeTimeText);
            form.Market = NormalizeSetupReviewText(form.Market, string.Empty, 120) ?? string.Empty;
            form.Direction = NormalizeSetupReviewText(form.Direction, "SHORT", 16) ?? "SHORT";
            form.Session = null;
            form.Status = "Nevyhodnoceno";
            form.Verdict = "Nevyhodnoceno";
            form.Outcome = NormalizeSetupReviewOutcome(form.Outcome);
            form.RMultiple = null;
            form.IsValid = null;
            form.Entry = null;
            form.StopLoss = null;
            form.TakeProfit = null;
            form.PlannedRr = null;
            form.HtfContextScore = null;
            form.ZoneScore = null;
            form.LocalStructureScore = null;
            form.EntryConfirmationScore = null;
            form.TimingScore = null;
            form.RiskRewardScore = null;
            form.SessionScore = null;
            form.ManagementScore = null;
            form.SetupRating = null;
            form.ProcessScore = null;
            form.MissingInfo = string.Empty;
            form.Notes = NormalizeSetupReviewText(form.Notes, string.Empty, 4000) ?? string.Empty;
            form.Aligned = string.Empty;
            form.Issues = string.Empty;
            form.NoTradeBlockers = string.Empty;
            form.AiReviewJson = null;
            form.ReviewedAt = null;

            if (string.IsNullOrWhiteSpace(form.StrategyKey) || !StrategyNoteKeys.Contains(form.StrategyKey))
                ModelState.AddModelError(nameof(TradeSetupReview.StrategyKey), "Vyber strategii.");

            if (form.TradeDate == default)
                ModelState.AddModelError(nameof(TradeSetupReview.TradeDate), "Doplň datum tradu.");

            if (string.IsNullOrWhiteSpace(form.Market))
                ModelState.AddModelError(nameof(TradeSetupReview.Market), "Doplň trh nebo symbol.");

            if (form.Direction is not ("LONG" or "SHORT"))
                ModelState.AddModelError(nameof(TradeSetupReview.Direction), "Vyber směr tradu.");

            if (string.IsNullOrWhiteSpace(form.TradeTimeText))
                ModelState.AddModelError(nameof(TradeSetupReview.TradeTimeText), "Doplň čas tradu.");
            else if (!TimeOnly.TryParse(form.TradeTimeText, CultureInfo.InvariantCulture, out _))
                ModelState.AddModelError(nameof(TradeSetupReview.TradeTimeText), "Čas tradu není platný.");

            if (string.IsNullOrWhiteSpace(form.Outcome) || !SetupReviewOutcomes.Contains(form.Outcome))
                ModelState.AddModelError(nameof(TradeSetupReview.Outcome), "Vyber výsledek tradu.");

            await ApplySetupReviewImagesAsync(form, imageFiles);

            if (!ModelState.IsValid)
            {
                var reviews = await _context.TradeSetupReviews
                    .AsNoTracking()
                    .Include(r => r.Images)
                    .Where(r => r.UserId == userId)
                    .OrderByDescending(r => r.TradeDate)
                    .ThenByDescending(r => r.TradeTimeText)
                    .ThenByDescending(r => r.CreatedAt)
                    .ToListAsync();

                return View(BuildSetupReviewViewModel(reviews, form));
            }

            _context.TradeSetupReviews.Add(form);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(SetupReview));
        }

        [HttpGet]
        public async Task<IActionResult> SetupReviewImage(int id)
        {
            var userId = CurrentUserId;
            var image = await _context.TradeSetupReviewImages
                .AsNoTracking()
                .Where(i => i.Id == id && i.TradeSetupReview.UserId == userId)
                .Select(i => new { i.ImageData, i.ImageContentType })
                .FirstOrDefaultAsync();

            if (image?.ImageData == null || string.IsNullOrWhiteSpace(image.ImageContentType))
                return NotFound();

            return File(image.ImageData, image.ImageContentType);
        }

        [HttpGet]
        public async Task<IActionResult> SetupReviewEditPayload(int id)
        {
            var review = await _context.TradeSetupReviews
                .AsNoTracking()
                .Where(r => r.Id == id && r.UserId == CurrentUserId)
                .FirstOrDefaultAsync();

            if (review is null)
                return NotFound();

            return Json(new
            {
                review.Id,
                strategyKey = review.StrategyKey,
                date = review.TradeDate.ToString("yyyy-MM-dd"),
                time = review.TradeTimeText,
                market = review.Market,
                direction = review.Direction,
                outcome = review.Outcome,
                outcomeLabel = FormatSetupReviewOutcome(review.Outcome),
                missingInfo = review.MissingInfo,
                issues = review.Issues,
                notes = review.Notes
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSetupReview(int id, [FromForm] SetupReviewEditRequest request)
        {
            var review = await _context.TradeSetupReviews
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == CurrentUserId);

            if (review is null)
                return NotFound();

            var strategyKey = NormalizeSetupReviewText(request.StrategyKey, null, 64);
            var direction = NormalizeSetupReviewText(request.Direction, null, 16)?.ToUpperInvariant();
            var market = NormalizeSetupReviewText(request.Market, string.Empty, 120) ?? string.Empty;
            var outcome = NormalizeSetupReviewOutcome(request.Outcome);
            var missingInfo = NormalizeSetupReviewText(request.MissingInfo, string.Empty, 4000) ?? string.Empty;
            var issues = NormalizeSetupReviewText(request.Issues, string.Empty, 4000) ?? string.Empty;
            var notes = NormalizeSetupReviewText(request.Notes, string.Empty, 4000) ?? string.Empty;

            if (strategyKey is null || !StrategyNoteKeys.Contains(strategyKey))
                ModelState.AddModelError(nameof(request.StrategyKey), "Vyber strategii.");

            if (!DateOnly.TryParseExact(request.TradeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeDate))
                ModelState.AddModelError(nameof(request.TradeDate), "Datum není platné.");

            if (!TimeOnly.TryParseExact(request.TradeTimeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeTime))
                ModelState.AddModelError(nameof(request.TradeTimeText), "Čas není platný.");

            if (direction is not ("LONG" or "SHORT"))
                ModelState.AddModelError(nameof(request.Direction), "Vyber směr tradu.");

            if (string.IsNullOrWhiteSpace(market))
                ModelState.AddModelError(nameof(request.Market), "Doplň trh nebo symbol.");

            if (outcome is null)
                ModelState.AddModelError(nameof(request.Outcome), "Vyber výsledek tradu.");

            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            review.StrategyKey = strategyKey!;
            review.TradeDate = tradeDate;
            review.TradeTimeText = tradeTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            review.Market = market;
            review.Direction = direction!;
            review.Outcome = outcome!;
            review.MissingInfo = missingInfo;
            review.Issues = issues;
            review.Notes = notes;
            review.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Json(new
            {
                review.Id,
                strategyKey = review.StrategyKey,
                date = review.TradeDate.ToString("yyyy-MM-dd"),
                time = review.TradeTimeText,
                market = review.Market,
                direction = review.Direction,
                outcome = review.Outcome,
                outcomeLabel = FormatSetupReviewOutcome(review.Outcome),
                missingInfo = review.MissingInfo,
                issues = review.Issues,
                notes = review.Notes
            });
        }

        [HttpGet]
        public async Task<IActionResult> SetupReviewAnalysisPayload(int id)
        {
            var userId = CurrentUserId;
            var review = await _context.TradeSetupReviews
                .AsNoTracking()
                .Include(r => r.Images)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (review is null)
                return NotFound();

            return Json(BuildSetupReviewAnalysisPayload(review));
        }

        [HttpGet]
        public async Task<IActionResult> SetupReviewUnreviewedPayload()
        {
            var userId = CurrentUserId;
            var reviews = await _context.TradeSetupReviews
                .AsNoTracking()
                .Include(r => r.Images)
                .Where(r => r.UserId == userId && r.Status == "Nevyhodnoceno")
                .OrderBy(r => r.TradeDate)
                .ThenBy(r => r.CreatedAt)
                .ToListAsync();

            return Json(reviews.Select(BuildSetupReviewAnalysisPayload).ToList());
        }

        [HttpGet]
        public async Task<IActionResult> SetupReviewAllAnalysisPayload()
        {
            var userId = CurrentUserId;
            var reviews = await _context.TradeSetupReviews
                .AsNoTracking()
                .Include(r => r.Images)
                .Where(r => r.UserId == userId)
                .OrderBy(r => r.TradeDate)
                .ThenBy(r => r.TradeTimeText)
                .ThenBy(r => r.CreatedAt)
                .ToListAsync();

            return Json(reviews.Select(BuildSetupReviewAnalysisPayload).ToList());
        }

        // GET: JournalEntries/Strategy
        public IActionResult Strategy()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> StrategyNote(string strategyKey, CancellationToken cancellationToken)
        {
            if (!IsValidStrategyNoteKey(strategyKey))
            {
                return BadRequest();
            }

            var userId = CurrentUserId;
            var note = await _context.StrategyNotes
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.StrategyKey == strategyKey)
                .Select(n => new
                {
                    n.ContentHtml,
                    n.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            return Json(new
            {
                contentHtml = note?.ContentHtml ?? string.Empty,
                updatedAt = note?.UpdatedAt
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStrategyNote([FromBody] StrategyNoteSaveRequest request, CancellationToken cancellationToken)
        {
            if (!IsValidStrategyNoteKey(request.StrategyKey))
            {
                return BadRequest();
            }

            var userId = CurrentUserId;
            var now = DateTimeOffset.UtcNow;
            var content = request.ContentHtml?.Trim() ?? string.Empty;

            if (content.Length > 200_000)
            {
                return BadRequest(new { message = "Poznámka je příliš dlouhá." });
            }

            var note = await _context.StrategyNotes
                .FirstOrDefaultAsync(n => n.UserId == userId && n.StrategyKey == request.StrategyKey, cancellationToken);

            if (note is null)
            {
                note = new StrategyNote
                {
                    UserId = userId,
                    StrategyKey = request.StrategyKey,
                    ContentHtml = content,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.StrategyNotes.Add(note);
            }
            else
            {
                note.ContentHtml = content;
                note.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Json(new
            {
                updatedAt = note.UpdatedAt
            });
        }

        private static bool IsValidStrategyNoteKey(string? strategyKey)
        {
            return !string.IsNullOrWhiteSpace(strategyKey) && StrategyNoteKeys.Contains(strategyKey);
        }

        public sealed class StrategyNoteSaveRequest
        {
            public string StrategyKey { get; set; } = string.Empty;

            public string? ContentHtml { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> SetupReviewRawJson(int id, CancellationToken cancellationToken)
        {
            var rawJson = await _context.TradeSetupReviews
                .AsNoTracking()
                .Where(r => r.Id == id && r.UserId == CurrentUserId)
                .Select(r => r.AiReviewJson)
                .FirstOrDefaultAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(rawJson)
                ? NotFound()
                : Content(rawJson, "application/json", System.Text.Encoding.UTF8);
        }

        private static string GetStrategyCode(string? strategyKey)
        {
            return strategyKey?.Trim().ToLowerInvariant() switch
            {
                "mucka" or "petr můčka" or "petr mucka" or "petr můčka s/d" or "corex" or "corex s/d" => "MUCKA",
                _ => strategyKey?.Trim().ToUpperInvariant() ?? string.Empty
            };
        }

        private static SetupReviewViewModel BuildSetupReviewViewModel(
            IReadOnlyList<TradeSetupReview> reviews,
            TradeSetupReview? form = null)
        {
            var completed = reviews
                .Where(r => r.RMultiple.HasValue)
                .ToList();
            var validKnown = reviews
                .Where(r => r.IsValid.HasValue)
                .ToList();
            var processScores = reviews
                .Select(review => review.ProcessScore ?? CalculateSetupReviewProcessScore(review))
                .Where(score => score.HasValue)
                .Select(score => score!.Value)
                .ToList();
            var wins = completed.Count(r => r.RMultiple > 0);
            var totalR = completed.Sum(r => r.RMultiple ?? 0);

            return new SetupReviewViewModel
            {
                Reviews = reviews,
                Form = form ?? new TradeSetupReview
                {
                    TradeDate = DateOnly.FromDateTime(DateTime.Today),
                    StrategyKey = "mucka",
                    Direction = "SHORT",
                    Status = "Nevyhodnoceno",
                    Verdict = "Nevyhodnoceno"
                },
                Stats = new SetupReviewStats
                {
                    TotalSetups = reviews.Count,
                    ReviewedSetups = reviews.Count(r => r.Status != "Nevyhodnoceno"),
                    CompletedSetups = completed.Count,
                    Wins = wins,
                    Losses = completed.Count - wins,
                    WinRate = completed.Count == 0 ? 0 : wins * 100m / completed.Count,
                    AverageR = completed.Count == 0 ? null : totalR / completed.Count,
                    TotalR = totalR,
                    ValidKnownSetups = validKnown.Count,
                    ValidSetups = validKnown.Count(r => r.IsValid == true),
                    ValidRate = validKnown.Count == 0 ? 0 : validKnown.Count(r => r.IsValid == true) * 100m / validKnown.Count,
                    AverageProcessScore = processScores.Count == 0 ? null : processScores.Average()
                }
            };
        }

        private static decimal? CalculateSetupReviewProcessScore(TradeSetupReview review)
        {
            var scores = new[]
            {
                review.HtfContextScore,
                review.ZoneScore,
                review.LocalStructureScore,
                review.EntryConfirmationScore,
                review.TimingScore,
                review.RiskRewardScore,
                review.SessionScore,
                review.ManagementScore
            }
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();

            if (scores.Count == 0)
                return null;

            return Math.Round(scores.Average() * 100m, 0);
        }

        private object BuildSetupReviewAnalysisPayload(TradeSetupReview review)
        {
            return new
            {
                review.Id,
                strategy = review.StrategyKey,
                date = review.TradeDate.ToString("yyyy-MM-dd"),
                time = review.TradeTimeText,
                market = review.Market,
                direction = review.Direction,
                review.Session,
                review.Status,
                review.Verdict,
                setupRating = review.SetupRating,
                processScore = review.ProcessScore ?? CalculateSetupReviewProcessScore(review),
                review.ReviewedAt,
                review.Outcome,
                r = review.RMultiple,
                review.IsValid,
                review.Entry,
                review.StopLoss,
                review.TakeProfit,
                review.PlannedRr,
                scorecard = new
                {
                    htfContext = review.HtfContextScore,
                    zone = review.ZoneScore,
                    localStructure = review.LocalStructureScore,
                    entryConfirmation = review.EntryConfirmationScore,
                    timing = review.TimingScore,
                    riskReward = review.RiskRewardScore,
                    session = review.SessionScore,
                    management = review.ManagementScore,
                    percent = CalculateSetupReviewProcessScore(review)
                },
                missingInfo = SplitSetupReviewLines(review.MissingInfo),
                aligned = SplitSetupReviewLines(review.Aligned),
                issues = SplitSetupReviewLines(review.Issues),
                noTradeBlockers = SplitSetupReviewLines(review.NoTradeBlockers),
                notes = review.Notes,
                rubric = GetStrategyCode(review.StrategyKey) == "MUCKA"
                    ? _muckaRubricProvider.GetRubric()
                    : (JsonElement?)null,
                images = review.Images.OrderBy(i => i.Id).Select(i => new
                {
                    i.Id,
                    i.ImageFileName,
                    url = Url.Action(nameof(SetupReviewImage), "JournalEntries", new { id = i.Id })
                }).ToList()
            };
        }

        private static string? NormalizeSetupReviewText(string? value, string? fallback, int maxLength)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

            if (normalized is null || normalized.Length <= maxLength)
                return normalized;

            return normalized[..maxLength];
        }

        private static string? NormalizeSetupReviewTime(string? value)
        {
            var normalized = NormalizeSetupReviewText(value, null, 40);

            if (normalized is null)
                return null;

            return TimeOnly.TryParse(normalized, CultureInfo.InvariantCulture, out var time)
                ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
                : normalized;
        }

        private static string? NormalizeSetupReviewOutcome(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToLowerInvariant() switch
            {
                "1" or "stoploss" or "stop loss" => "1",
                "2" or "breakeven" or "break even" => "2",
                "3" or "take profit" or "takeprofit" => "3",
                _ => null
            };
        }

        private static string FormatSetupReviewOutcome(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "1" or "stoploss" or "stop loss" => "Stoploss",
                "2" or "breakeven" or "break even" => "Breakeven",
                "3" or "take profit" or "takeprofit" => "Take profit",
                _ => string.IsNullOrWhiteSpace(value) ? "-" : value
            };
        }

        private static IReadOnlyList<string> SplitSetupReviewLines(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private void ApplySetupReviewDecimalFormValues(TradeSetupReview review)
        {
            if (TryReadDecimalFromForm(nameof(TradeSetupReview.Entry), out var entry))
                review.Entry = entry;

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.StopLoss), out var stopLoss))
                review.StopLoss = stopLoss;

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.TakeProfit), out var takeProfit))
                review.TakeProfit = takeProfit;

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.PlannedRr), out var plannedRr))
                review.PlannedRr = plannedRr;

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.RMultiple), out var rMultiple))
                review.RMultiple = rMultiple;

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.HtfContextScore), out var htfContext))
                review.HtfContextScore = ClampSetupReviewScore(htfContext);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.ZoneScore), out var zone))
                review.ZoneScore = ClampSetupReviewScore(zone);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.LocalStructureScore), out var localStructure))
                review.LocalStructureScore = ClampSetupReviewScore(localStructure);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.EntryConfirmationScore), out var entryConfirmation))
                review.EntryConfirmationScore = ClampSetupReviewScore(entryConfirmation);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.TimingScore), out var timing))
                review.TimingScore = ClampSetupReviewScore(timing);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.RiskRewardScore), out var riskReward))
                review.RiskRewardScore = ClampSetupReviewScore(riskReward);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.SessionScore), out var session))
                review.SessionScore = ClampSetupReviewScore(session);

            if (TryReadDecimalFromForm(nameof(TradeSetupReview.ManagementScore), out var management))
                review.ManagementScore = ClampSetupReviewScore(management);
        }

        private static decimal? ClampSetupReviewScore(decimal? value)
        {
            if (!value.HasValue)
                return null;

            return Math.Clamp(value.Value, 0m, 1m);
        }

        private async Task ApplySetupReviewImagesAsync(TradeSetupReview review, IReadOnlyList<IFormFile>? imageFiles)
        {
            var files = imageFiles?
                .Where(file => file.Length > 0)
                .ToList() ?? [];

            if (files.Count == 0)
            {
                ModelState.AddModelError("imageFiles", "Přidej alespoň jednu fotku setupu.");
                return;
            }

            if (files.Count > 12)
            {
                ModelState.AddModelError("", "Nahraj maximálně 12 obrázků k jednomu setup review.");
                return;
            }

            const long maxBytes = 8 * 1024 * 1024;
            foreach (var imageFile in files)
            {
                if (imageFile.Length > maxBytes)
                {
                    ModelState.AddModelError("", $"Obrázek {imageFile.FileName} je příliš velký (max 8 MB).");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(imageFile.ContentType) || !imageFile.ContentType.StartsWith("image/"))
                {
                    ModelState.AddModelError("", $"Soubor {imageFile.FileName} musí být obrázek.");
                    continue;
                }

                using var ms = new MemoryStream();
                await imageFile.CopyToAsync(ms);

                review.Images.Add(new TradeSetupReviewImage
                {
                    ImageData = ms.ToArray(),
                    ImageContentType = imageFile.ContentType,
                    ImageFileName = Path.GetFileName(imageFile.FileName),
                    Label = Path.GetFileNameWithoutExtension(imageFile.FileName),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        // GET: JournalEntries/Analysis
        public async Task<IActionResult> Analysis()
        {
            var userId = CurrentUserId;

            var entries = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.JournalId == CurrentJournalId)
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
            bool planOnly = false,
            DateOnly? month = null,
            DateOnly? selectedDate = null)
        {
            var userId = CurrentUserId;

            var allUserEntries = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Images)
                .Where(e => e.UserId == userId && e.JournalId == CurrentJournalId)
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

            var localToday = DateOnly.FromDateTime(DateTime.Now);
            var selectedCalendarDate = selectedDate ?? entries
                .Select(e => DateOnly.FromDateTime((e.EntryTime ?? e.CreatedAt).LocalDateTime))
                .DefaultIfEmpty(localToday)
                .Max();
            var calendarMonth = month ?? new DateOnly(selectedCalendarDate.Year, selectedCalendarDate.Month, 1);
            calendarMonth = new DateOnly(calendarMonth.Year, calendarMonth.Month, 1);
            selectedCalendarDate = selectedDate ?? selectedCalendarDate;

            var calendar = BuildJournalCalendar(entries, calendarMonth, selectedCalendarDate);
            var selectedDayEntries = entries
                .Where(e => DateOnly.FromDateTime((e.EntryTime ?? e.CreatedAt).LocalDateTime) == selectedCalendarDate)
                .OrderBy(e => e.EntryTime ?? e.CreatedAt)
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
                SelectedDayEntries = selectedDayEntries,
                Filters = new JournalIndexFilters
                {
                    Asset = asset,
                    Outcome = outcome,
                    Side = side,
                    PlanOnly = planOnly,
                    Month = calendarMonth,
                    SelectedDate = selectedCalendarDate
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
                Calendar = calendar,
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

        private static JournalCalendar BuildJournalCalendar(IReadOnlyList<JournalEntry> entries, DateOnly month, DateOnly selectedDate)
        {
            var firstOfMonth = new DateOnly(month.Year, month.Month, 1);
            var daysBeforeMonth = (int)firstOfMonth.DayOfWeek;
            var firstVisibleDay = firstOfMonth.AddDays(-daysBeforeMonth);
            var days = new List<JournalCalendarDay>(42);

            var daySummaries = entries
                .GroupBy(e => DateOnly.FromDateTime((e.EntryTime ?? e.CreatedAt).LocalDateTime))
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        TradeCount = g.Count(),
                        NetMove = g.Sum(entry => CalculateTradeRMultiple(entry) ?? 0)
                    });

            for (var i = 0; i < 42; i++)
            {
                var date = firstVisibleDay.AddDays(i);
                daySummaries.TryGetValue(date, out var summary);

                days.Add(new JournalCalendarDay
                {
                    Date = date,
                    IsCurrentMonth = date.Month == month.Month && date.Year == month.Year,
                    IsSelected = date == selectedDate,
                    TradeCount = summary?.TradeCount ?? 0,
                    NetMove = summary?.NetMove ?? 0
                });
            }

            return new JournalCalendar
            {
                Month = month,
                PreviousMonth = firstOfMonth.AddMonths(-1),
                NextMonth = firstOfMonth.AddMonths(1),
                SelectedDate = selectedDate,
                Days = days
            };
        }

        private static decimal? CalculateTradeRMultiple(JournalEntry entry)
        {
            return entry.Outcome switch
            {
                TradeOutcome.Win => 1m,
                TradeOutcome.Loss => -1m,
                TradeOutcome.Breakeven => 0m,
                _ => null
            };
        }

        // GET: JournalEntries/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Images)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId);

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
                .Where(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId)
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
                .Where(i => i.Id == id && i.JournalEntry.UserId == userId && i.JournalEntry.JournalId == CurrentJournalId)
                .Select(i => new { i.ImageData, i.ImageContentType })
                .FirstOrDefaultAsync();

            if (image?.ImageData == null || string.IsNullOrWhiteSpace(image.ImageContentType))
                return NotFound();

            return File(image.ImageData, image.ImageContentType);
        }

        // POST: JournalEntries/DeleteGalleryImage/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGalleryImage(int id)
        {
            var userId = CurrentUserId;

            var image = await _context.JournalEntryImages
                .Include(i => i.JournalEntry)
                .FirstOrDefaultAsync(i => i.Id == id && i.JournalEntry.UserId == userId && i.JournalEntry.JournalId == CurrentJournalId);

            if (image == null)
                return NotFound(new { message = "Obrázek se nepodařilo najít." });

            var entryId = image.JournalEntryId;

            _context.JournalEntryImages.Remove(image);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                remainingCount = await CountEntryImagesAsync(entryId, userId)
            });
        }

        // POST: JournalEntries/DeleteLegacyImage/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLegacyImage(int id)
        {
            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId);

            if (entry == null)
                return NotFound(new { message = "Záznam se nepodařilo najít." });

            entry.ImageData = null;
            entry.ImageContentType = null;
            entry.ImageFileName = null;

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                remainingCount = await CountEntryImagesAsync(id, userId)
            });
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
            entry.JournalId = CurrentJournalId;
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
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId);

            if (entry == null) return NotFound();

            return View(entry);
        }

        // POST: JournalEntries/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            JournalEntry form,
            IReadOnlyList<IFormFile>? imageFiles,
            IFormFile? imageFile)
        {
            ApplyDecimalFormValues(form);

            var userId = CurrentUserId;

            // Načteme původní záznam patřící uživateli
            var entry = await _context.JournalEntries
                .Include(e => e.Images)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId);

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

            // Nové uploady se přidají do galerie.
            await ApplyImageUploadsAsync(entry, imageFiles, imageFile);

            if (!ModelState.IsValid)
                return View(entry);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task<int> CountEntryImagesAsync(int entryId, string userId)
        {
            var galleryCount = await _context.JournalEntryImages
                .CountAsync(i => i.JournalEntryId == entryId && i.JournalEntry.UserId == userId && i.JournalEntry.JournalId == CurrentJournalId);

            var hasLegacyImage = await _context.JournalEntries
                .AnyAsync(e => e.Id == entryId && e.UserId == userId && e.JournalId == CurrentJournalId && e.ImageData != null);

            return galleryCount + (hasLegacyImage ? 1 : 0);
        }

        // GET: JournalEntries/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = CurrentUserId;

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(e => e.Images)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId);

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
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.JournalId == CurrentJournalId);

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
