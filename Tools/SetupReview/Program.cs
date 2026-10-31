using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.Services;
using TradingJournal.ViewModels;

var repoRoot = Directory.GetCurrentDirectory();
var configuration = new ConfigurationBuilder()
    .SetBasePath(repoRoot)
    .AddJsonFile("TradingJournal/appsettings.json", optional: true)
    .AddJsonFile("TradingJournal/appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = Environment.GetEnvironmentVariable("TRADINGJOURNAL_CONNECTION_STRING")
    ?? configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("TradingJournal connection string was not found.");

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(10),
        errorNumbersToAdd: null))
    .Options;

await using var db = new ApplicationDbContext(options);
var action = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "export";

switch (action)
{
    case "export":
        await ExportPendingAsync(db, repoRoot);
        break;
    case "import":
        await ImportResultsAsync(db, repoRoot);
        break;
    default:
        throw new ArgumentException("Use 'export' or 'import'.");
}

static async Task ExportPendingAsync(ApplicationDbContext db, string repoRoot)
{
    var packageRoot = Path.Combine(repoRoot, "tmp", "setup-review");
    var pendingRoot = Path.Combine(packageRoot, "pending");
    Directory.CreateDirectory(pendingRoot);
    foreach (var directory in Directory.EnumerateDirectories(pendingRoot))
        Directory.Delete(directory, recursive: true);

    var reviews = await db.TradeSetupReviews
        .AsNoTracking()
        .Include(review => review.Images)
        .Where(review => review.Status == "Nevyhodnoceno")
        .OrderByDescending(review => review.TradeDate)
        .ThenByDescending(review => review.TradeTimeText)
        .ThenByDescending(review => review.CreatedAt)
        .ToListAsync();

    var manifestReviews = new List<object>();
    foreach (var review in reviews)
    {
        var reviewRoot = Path.Combine(pendingRoot, review.Id.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(reviewRoot);
        var imageFiles = new List<string>();
        var imageIndex = 1;

        foreach (var image in review.Images.OrderBy(image => image.Id))
        {
            var extension = image.ImageContentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".bin"
            };
            var fileName = $"image-{imageIndex:00}{extension}";
            await File.WriteAllBytesAsync(Path.Combine(reviewRoot, fileName), image.ImageData);
            imageFiles.Add($"tmp/setup-review/pending/{review.Id}/{fileName}");
            imageIndex++;
        }

        manifestReviews.Add(new
        {
            tradeId = review.Id,
            strategy = GetStrategyCode(review.StrategyKey) == "MUCKA" ? "Petr Můčka strategie" : review.StrategyKey,
            strategyCode = GetStrategyCode(review.StrategyKey),
            symbol = review.Market,
            tradeDate = review.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tradeTime = review.TradeTimeText,
            direction = review.Direction,
            result = GetOutcomeLabel(review.Outcome),
            note = review.Notes,
            imageFiles,
            currentStatus = review.Status
        });
    }

    var manifest = new
    {
        generatedAt = DateTimeOffset.UtcNow,
        rubricPath = "TradingJournal/Docs/strategies/petr-mucka/rubric.json",
        reviews = manifestReviews
    };
    var manifestPath = Path.Combine(pendingRoot, "manifest.json");
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, GetJsonOptions()));
    Console.WriteLine(JsonSerializer.Serialize(new { action = "export", count = reviews.Count, manifest = manifestPath }, GetJsonOptions()));
}

static async Task ImportResultsAsync(ApplicationDbContext db, string repoRoot)
{
    var resultsPath = Path.Combine(repoRoot, "tmp", "setup-review", "results.json");
    if (!File.Exists(resultsPath))
        throw new FileNotFoundException("Codex results file was not found.", resultsPath);

    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(resultsPath));
    var elements = document.RootElement.ValueKind == JsonValueKind.Array
        ? document.RootElement.EnumerateArray().ToList()
        : document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().ToList()
            : [];
    var imported = new List<object>();

    foreach (var element in elements)
    {
        var result = JsonSerializer.Deserialize<SetupReviewAnalysisResult>(element.GetRawText(), GetJsonOptions());
        if (result is null || result.TradeId <= 0)
            continue;

        var review = await db.TradeSetupReviews
            .FirstOrDefaultAsync(item => item.Id == result.TradeId && item.Status == "Nevyhodnoceno");
        if (review is null)
            continue;

        var verdict = NormalizeVerdict(result.Verdict);
        review.Status = verdict == "Nelze vyhodnotit" ? "NelzeVyhodnotit" : "Vyhodnoceno";
        review.Verdict = verdict;
        review.IsValid = verdict switch
        {
            "Validní" => true,
            "Nevalidní" => false,
            _ => null
        };
        review.SetupRating = string.IsNullOrWhiteSpace(result.SetupRating) ? "No trade" : result.SetupRating.Trim();
        review.ProcessScore = result.ProcessScore.HasValue ? Math.Clamp(result.ProcessScore.Value, 0m, 100m) : null;
        review.MissingInfo = JoinLines(result.MissingInformation);
        review.Aligned = JoinLines(result.WhatWasCorrect);
        review.Issues = JoinLines(result.WhatWasWrong);
        review.NoTradeBlockers = JoinLines(result.NoTradeBlockers);
        review.Notes = result.ImprovementNotes?.Trim() ?? string.Empty;
        review.Entry = result.PlannedEntry;
        review.StopLoss = result.PlannedStopLoss;
        review.TakeProfit = result.PlannedTakeProfit;
        review.PlannedRr = result.PlannedRRR;
        if (result.FinalResultR.HasValue)
            review.RMultiple = result.FinalResultR;
        review.AiReviewJson = element.GetRawText();
        review.ReviewedAt = DateTimeOffset.UtcNow;
        review.UpdatedAt = DateTimeOffset.UtcNow;
        imported.Add(new { tradeId = review.Id, status = review.Status, verdict = review.Verdict });
    }

    await db.SaveChangesAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { action = "import", count = imported.Count, reviews = imported }, GetJsonOptions()));
}

static string GetStrategyCode(string? key) => key?.Trim().ToLowerInvariant() switch
{
    "mucka" or "petr můčka" or "petr mucka" or "petr můčka s/d" or "corex" or "corex s/d" => "MUCKA",
    _ => key?.Trim().ToUpperInvariant() ?? string.Empty
};

static string GetOutcomeLabel(string? outcome) => outcome?.Trim().ToLowerInvariant() switch
{
    "1" => "Stoploss",
    "2" => "Breakeven",
    "3" => "Take profit",
    _ => outcome ?? string.Empty
};

static string NormalizeVerdict(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
{
    "validní" or "validni" => "Validní",
    "částečně validní" or "castecne validni" => "Částečně validní",
    "nevalidní" or "nevalidni" => "Nevalidní",
    _ => "Nelze vyhodnotit"
};

static string JoinLines(IEnumerable<string>? values) => string.Join(
    Environment.NewLine,
    (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));

static JsonSerializerOptions GetJsonOptions() => new(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};
