using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Services;

public sealed class SeasonalityService
{
    public static IReadOnlyList<SeasonalityAsset> Assets { get; } =
    [
        new("EURUSD", "EUR/USD", "EURUSD=X", "eurusd"),
        new("XAUUSD", "Gold / XAU/USD", "GC=F", "xauusd"),
        new("BTCUSD", "Bitcoin / BTC/USD", "BTC-USD", "btcusd"),
        new("NDX", "Nasdaq-100", "^NDX", "^ndx")
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SeasonalityService> _logger;
    private readonly ApplicationDbContext? _context;

    public SeasonalityService(IHttpClientFactory httpClientFactory, ILogger<SeasonalityService> logger)
        : this(httpClientFactory, logger, null)
    {
    }

    public SeasonalityService(IHttpClientFactory httpClientFactory, ILogger<SeasonalityService> logger, ApplicationDbContext? context)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _context = context;
    }

    public async Task<SeasonalityViewModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_context is not null)
        {
            return new SeasonalityViewModel { Assets = Assets, Prices = await LoadCachedPricesAsync(cancellationToken) };
        }

        return await DownloadAsync(cancellationToken);
    }

    public async Task<SeasonalityViewModel> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        return new SeasonalityViewModel
        {
            Assets = Assets,
            Prices = _context is null ? new Dictionary<string, IReadOnlyList<SeasonalityPrice>>() : await LoadCachedPricesAsync(cancellationToken)
        };
    }

    public async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null) throw new InvalidOperationException("Seasonality database context is not configured.");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingJournal-Seasonality/1.0");
        foreach (var asset in Assets)
        {
            var cached = await _context.SeasonalityPrices
                .Where(item => item.AssetKey == asset.Key)
                .ToDictionaryAsync(item => item.Date, cancellationToken);
            var period1 = cached.Count == 0
                ? 0
                : new DateTimeOffset(cached.Keys.Max().ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1).ToUnixTimeSeconds();
            try
            {
                var yahoo = await client.GetStringAsync(
                    $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(asset.YahooSymbol)}?period1={period1}&period2={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}&interval=1d&events=history",
                    cancellationToken);
                await UpsertAsync(asset.Key, "Yahoo", ParseYahoo(yahoo), cached, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yahoo seasonality data load failed for {Asset}; trying Stooq.", asset.Key);
                try
                {
                    var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={asset.StooqSymbol}&i=d", cancellationToken);
                    await UpsertAsync(asset.Key, "Stooq", ParseStooq(csv), cached, cancellationToken);
                }
                catch (Exception fallbackException)
                {
                    _logger.LogError(fallbackException, "Seasonality data load failed for {Asset}.", asset.Key);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<SeasonalityViewModel> DownloadAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingJournal-Seasonality/1.0");
        var prices = new Dictionary<string, IReadOnlyList<SeasonalityPrice>>();
        foreach (var asset in Assets)
        {
            try
            {
                var yahoo = await client.GetStringAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(asset.YahooSymbol)}?period1=0&period2={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}&interval=1d&events=history", cancellationToken);
                prices[asset.Key] = ParseYahoo(yahoo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yahoo seasonality data load failed for {Asset}; trying Stooq.", asset.Key);
                try
                {
                    var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={asset.StooqSymbol}&i=d", cancellationToken);
                    prices[asset.Key] = ParseStooq(csv);
                }
                catch (Exception fallbackException)
                {
                    _logger.LogError(fallbackException, "Seasonality data load failed for {Asset}.", asset.Key);
                    prices[asset.Key] = [];
                }
            }
        }
        return new SeasonalityViewModel { Assets = Assets, Prices = prices };
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<SeasonalityPrice>>> LoadCachedPricesAsync(CancellationToken cancellationToken)
    {
        var rows = await _context!.SeasonalityPrices.AsNoTracking().OrderBy(item => item.Date).ToListAsync(cancellationToken);
        return rows.GroupBy(item => item.AssetKey).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<SeasonalityPrice>)group.Select(item => new SeasonalityPrice(item.Date, item.Close)).ToList());
    }

    private async Task UpsertAsync(string assetKey, string source, IReadOnlyList<SeasonalityPrice> prices, Dictionary<DateOnly, SeasonalityPriceEntity> cached, CancellationToken cancellationToken)
    {
        foreach (var price in prices)
        {
            if (cached.TryGetValue(price.Date, out var existing))
            {
                existing.Close = price.Close;
                existing.Source = source;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _context!.SeasonalityPrices.Add(new SeasonalityPriceEntity
                {
                    AssetKey = assetKey,
                    Date = price.Date,
                    Close = price.Close,
                    Source = source,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }
        await _context!.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<SeasonalityPrice> ParseStooq(string csv)
    {
        var result = new List<SeasonalityPrice>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var fields = line.Trim().Split(',');
            if (fields.Length < 5 || !DateOnly.TryParse(fields[0], CultureInfo.InvariantCulture, out var date) ||
                !decimal.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                continue;
            result.Add(new SeasonalityPrice(date, close));
        }
        return result.OrderBy(item => item.Date).ToList();
    }

    public static IReadOnlyList<SeasonalityPrice> ParseYahoo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<SeasonalityPrice>();
        var chart = document.RootElement.GetProperty("chart").GetProperty("result")[0];
        var timestamps = chart.GetProperty("timestamp");
        var closes = chart.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
        for (var index = 0; index < timestamps.GetArrayLength(); index++)
        {
            if (timestamps[index].ValueKind != JsonValueKind.Number || closes[index].ValueKind is JsonValueKind.Null)
                continue;
            result.Add(new SeasonalityPrice(
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[index].GetInt64()).UtcDateTime),
                closes[index].GetDecimal()));
        }
        return result.Where(item => item.Close > 0).DistinctBy(item => item.Date).OrderBy(item => item.Date).ToList();
    }
}
