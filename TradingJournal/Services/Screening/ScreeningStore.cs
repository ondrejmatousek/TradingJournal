using System.Security.Cryptography;
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
}
public sealed record ScreeningMarketCache(DateTimeOffset RetrievedAt, List<TradingJournal.Models.SeasonalityPrice> Prices, FundamentalInput? Fundamentals, string? Message);
