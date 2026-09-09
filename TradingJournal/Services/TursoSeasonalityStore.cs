using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TradingJournal.Models;

namespace TradingJournal.Services;

/// Stores the high-volume seasonality history in Turso without changing the
/// PostgreSQL-backed identity and application schema.
public sealed class TursoSeasonalityStore
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TursoSeasonalityStore> _logger;
    private readonly string? _endpoint;
    private readonly string? _token;

    public TursoSeasonalityStore(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<TursoSeasonalityStore> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var url = configuration["Turso:DatabaseUrl"] ?? configuration["TURSO_DATABASE_URL"];
        _token = configuration["Turso:AuthToken"] ?? configuration["TURSO_AUTH_TOKEN"];
        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(_token))
        {
            var normalized = url.Trim().Replace("libsql://", "https://", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
            _endpoint = normalized.EndsWith("/v2/pipeline", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + "/v2/pipeline";
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint) && !string.IsNullOrWhiteSpace(_token);

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return;
        await ExecuteAsync("CREATE TABLE IF NOT EXISTS SeasonalityPrices (AssetKey TEXT NOT NULL, Date TEXT NOT NULL, Close REAL NOT NULL, Source TEXT NOT NULL, UpdatedAt TEXT NOT NULL, PRIMARY KEY (AssetKey, Date))", [], cancellationToken);
    }

    public async Task<IReadOnlyList<SeasonalityPrice>> LoadAsync(string assetKey, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return [];
        var result = await ExecuteAsync("SELECT Date, Close FROM SeasonalityPrices WHERE AssetKey = ? ORDER BY Date", [TextArg(assetKey)], cancellationToken);
        return ReadRows(result).Select(row => new SeasonalityPrice(DateOnly.Parse(row[0], CultureInfo.InvariantCulture), decimal.Parse(row[1], CultureInfo.InvariantCulture))).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SeasonalityPrice>>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return new Dictionary<string, IReadOnlyList<SeasonalityPrice>>();
        var result = await ExecuteAsync("SELECT AssetKey, Date, Close FROM SeasonalityPrices ORDER BY AssetKey, Date", [], cancellationToken);
        return ReadRows(result).GroupBy(row => row[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SeasonalityPrice>)g.Select(row => new SeasonalityPrice(DateOnly.Parse(row[1], CultureInfo.InvariantCulture), decimal.Parse(row[2], CultureInfo.InvariantCulture))).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task UpsertAsync(string assetKey, string source, IReadOnlyList<SeasonalityPrice> prices, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || prices.Count == 0) return;
        var requests = prices.DistinctBy(x => x.Date).Select(price => new Request("INSERT INTO SeasonalityPrices (AssetKey, Date, Close, Source, UpdatedAt) VALUES (?, ?, ?, ?, ?) ON CONFLICT(AssetKey, Date) DO UPDATE SET Close=excluded.Close, Source=excluded.Source, UpdatedAt=excluded.UpdatedAt", [TextArg(assetKey), TextArg(price.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), FloatArg(price.Close), TextArg(source), TextArg(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))])).ToList();
        await ExecuteBatchAsync(requests, cancellationToken);
    }

    private async Task<JsonElement> ExecuteAsync(string sql, IReadOnlyList<JsonElement> args, CancellationToken cancellationToken)
        => (await ExecuteBatchAsync([new Request(sql, args)], cancellationToken)).GetProperty("results")[0];

    private async Task<JsonElement> ExecuteBatchAsync(IReadOnlyList<Request> requests, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Turso is not configured.");
        var operations = requests.Select(r => (object)new { type = "execute", stmt = new { sql = r.Sql, args = r.Args } }).ToList();
        operations.Add(new { type = "close" });
        var payload = new { requests = operations };
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Turso request failed ({(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(body);
        foreach (var result in document.RootElement.GetProperty("results").EnumerateArray())
            if (result.GetProperty("type").GetString() == "error") throw new InvalidOperationException("Turso rejected a seasonality statement.");
        return document.RootElement.Clone();
    }

    private static IEnumerable<string[]> ReadRows(JsonElement result)
    {
        var execute = result.GetProperty("response").GetProperty("result");
        foreach (var row in execute.GetProperty("rows").EnumerateArray())
            yield return row.EnumerateArray().Select(cell => cell.TryGetProperty("value", out var value) ? value.GetString() ?? "" : cell.ToString()).ToArray();
    }

    private static JsonElement TextArg(string value) => JsonSerializer.SerializeToElement(new { type = "text", value });
    private static JsonElement FloatArg(decimal value) => JsonSerializer.SerializeToElement(new { type = "float", value = value.ToString(CultureInfo.InvariantCulture) });
    private sealed record Request(string Sql, IReadOnlyList<JsonElement> Args);
}
