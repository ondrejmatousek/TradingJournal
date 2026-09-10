using System.Security.Cryptography;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;

namespace TradingJournal.Services.Screening;

// Production uses SQL through a scope per operation; explicitly constructed test stores may use files.
public sealed class ScreeningStore(IWebHostEnvironment environment, IServiceScopeFactory? scopes = null)
{
    private readonly string directory = Path.Combine(environment.ContentRootPath, "App_Data", "screening");
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string PathFor(string key) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    private static string IdFor(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public async Task<T?> ReadAsync<T>(string key, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (scopes != null)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var id = IdFor(key);
                var document = await db.ScreeningDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (document != null) return JsonSerializer.Deserialize<T>(document.Json, Json);
                // Import a legacy file only when SQL has no value. Never fall back on a SQL failure.
                var legacyPath = PathFor(key);
                if (!File.Exists(legacyPath)) return default;
                var legacy = await File.ReadAllTextAsync(legacyPath, ct);
                var value = JsonSerializer.Deserialize<T>(legacy, Json);
                db.ScreeningDocuments.Add(new() { Id = id, Json = legacy, UpdatedAt = DateTimeOffset.UtcNow });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    // Another web/worker instance may have inserted newer data during the import.
                    var winner = await db.ScreeningDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                    if (winner == null) throw;
                    return JsonSerializer.Deserialize<T>(winner.Json, Json);
                }
                return value;
            }
            var path = PathFor(key);
            if (!File.Exists(path)) return default;
            try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json); }
            catch (JsonException) { return default; }
        }
        finally { gate.Release(); }
    }
    public async Task WriteAsync<T>(string key, T value, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (scopes != null)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var id = IdFor(key);
                var json = JsonSerializer.Serialize(value, Json);
                var now = DateTimeOffset.UtcNow;
                var query = db.ScreeningDocuments.Where(x => x.Id == id);
                if (await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.Json, json).SetProperty(x => x.UpdatedAt, now), ct) == 0)
                {
                    db.ScreeningDocuments.Add(new() { Id = id, Json = json, UpdatedAt = now });
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateException)
                    {
                        if (await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.Json, json).SetProperty(x => x.UpdatedAt, now), ct) == 0) throw;
                    }
                }
                return;
            }
            Directory.CreateDirectory(directory);
            var path = PathFor(key);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, Json), ct);
            File.Move(temp, path, true);
        }
        finally { gate.Release(); }
    }

    public async Task WriteValuationAsync(string symbol, DateOnly date, ScreeningRow row, CancellationToken ct)
    {
        var valuation = row.Valuation;
        if (valuation == null) return;
        if (scopes != null)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var existing = await db.CompanyValuations.SingleOrDefaultAsync(x => x.Symbol == symbol && x.ValuationDate == date, ct);
                var entity = existing ?? new CompanyValuation { Symbol = symbol, ValuationDate = date };
                entity.CalculatedAt = DateTimeOffset.UtcNow; entity.CurrentPrice = row.Price;
                entity.FairValueLow = valuation.Low; entity.FairValueBase = valuation.Base; entity.FairValueHigh = valuation.High;
                entity.UpsidePercent = row.Price > 0 ? (valuation.Base / row.Price.Value - 1) * 100 : null;
                entity.MarginOfSafetyPercent = valuation.MarginOfSafety; entity.ValuationScore = valuation.ValuationScore; entity.Confidence = valuation.Confidence;
                entity.Status = valuation.Status; entity.Model = valuation.Model; entity.DataQuality = valuation.DataQuality;
            entity.MethodsJson = JsonSerializer.Serialize(new { valuation.DcfFairValue, valuation.RelativeFairValue, valuation.FcfFairValue, valuation.EvEbitdaFairValue, valuation.ForwardPeFairValue, valuation.CompositeFairValue, valuation.RelativeMultiple, valuation.Weights });
                entity.AssumptionsJson = JsonSerializer.Serialize(new { valuation.Growth, valuation.DiscountRate, valuation.TerminalGrowth, valuation.RiskFreeRate, valuation.Beta, valuation.EquityRiskPremium, valuation.CostOfDebt, valuation.TaxRate, valuation.WaccFallbackUsed });
                if (existing == null) db.CompanyValuations.Add(entity);
                await db.SaveChangesAsync(ct);
            }
            catch (DbException) when (!ct.IsCancellationRequested)
            {
                // SQL Server/local databases without the Postgres migration continue using the JSON cache.
            }
        }
        await WriteAsync($"valuation-history-{symbol}-{date:yyyyMMdd}", valuation, ct);
    }

    public async Task<List<CompanyValuation>?> ReadValuationsAsync(string symbol, DateOnly from, CancellationToken ct)
    {
        if (scopes == null) return null;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.CompanyValuations.AsNoTracking().Where(x => x.Symbol == symbol && x.ValuationDate >= from)
                .OrderBy(x => x.ValuationDate).ToListAsync(ct);
        }
        catch (DbException) when (!ct.IsCancellationRequested) { return null; }
    }

    public async Task<decimal?> ReadHistoricalRelativeMultipleAsync(string symbol, DateOnly before, CancellationToken ct)
    {
        var rows = await ReadValuationsAsync(symbol, before.AddYears(-5), ct);
        if (rows == null) return null;
        var values = rows.Where(x => x.ValuationDate < before).Select(x =>
        {
            try { return JsonDocument.Parse(x.MethodsJson).RootElement.TryGetProperty("relativeMultiple", out var p) && p.TryGetDecimal(out var v) ? (decimal?)v : null; }
            catch (JsonException) { return null; }
        }).Where(x => x is > 3 and < 60).Select(x => x!.Value).Order().ToArray();
        return values.Length == 0 ? null : ScreeningCalculator.Median(values);
    }
}
public sealed record ScreeningMarketCache(DateTimeOffset RetrievedAt, List<TradingJournal.Models.SeasonalityPrice> Prices, FundamentalInput? Fundamentals, string? Message)
{
    public ValuationResult? Valuation { get; init; }
}
