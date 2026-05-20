using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Services
{
    public class MarketBiasService
    {
        private static readonly MarketDefinition[] Markets =
        [
            new("XAU", "XAUUSD", "GC=F", ["xauusd", "gc.f"]),
            new("NQ", "Nasdaq 100", "NQ=F", ["nq.f", "mnq.f", "qqq.us"]),
            new("BTC", "Bitcoin", "BTC-USD", ["btcusd", "btcusd.pl"])
        ];

        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MarketBiasService> _logger;

        public MarketBiasService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<MarketBiasService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<HomeIndexViewModel> GetHomeMarketBiasAsync(CancellationToken cancellationToken = default, bool forceRefresh = false)
        {
            var today = DateTime.UtcNow.Date;
            var cards = new List<MarketBiasCard>();

            if (forceRefresh)
            {
                var symbols = Markets.Select(m => m.Symbol).ToArray();
                await _context.MarketBiases
                    .Where(b => b.BiasDate == today && symbols.Contains(b.Symbol))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            foreach (var market in Markets)
            {
                var bias = await _context.MarketBiases
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Symbol == market.Symbol && b.BiasDate == today, cancellationToken);

                if (bias is null)
                {
                    bias = await TryRefreshMarketAsync(market, today, cancellationToken)
                        ?? await _context.MarketBiases
                            .AsNoTracking()
                            .Where(b => b.Symbol == market.Symbol)
                            .OrderByDescending(b => b.BiasDate)
                            .ThenByDescending(b => b.CreatedAt)
                            .FirstOrDefaultAsync(cancellationToken)
                        ?? CreateUnavailableBias(market, today);
                }

                cards.Add(ToCard(bias));
            }

            return new HomeIndexViewModel
            {
                MarketBiases = cards,
                MarketBiasUpdatedAt = cards.Count == 0
                    ? null
                    : await _context.MarketBiases
                        .AsNoTracking()
                        .Where(b => cards.Select(c => c.Symbol).Contains(b.Symbol))
                        .OrderByDescending(b => b.CreatedAt)
                        .Select(b => (DateTimeOffset?)b.CreatedAt)
                        .FirstOrDefaultAsync(cancellationToken),
                MarketBiasIsLive = cards.All(c => !string.Equals(c.Source, "Fallback", StringComparison.OrdinalIgnoreCase))
            };
        }

        private async Task<MarketBias?> TryRefreshMarketAsync(MarketDefinition market, DateTime today, CancellationToken cancellationToken)
        {
            foreach (var stooqSymbol in market.StooqSymbols)
            {
                try
                {
                    var candles = await FetchYahooDailyCandlesAsync(market.YahooSymbol, cancellationToken);
                    if (candles.Count < 55)
                        throw new InvalidOperationException($"Yahoo returned only {candles.Count} candles.");

                    var bias = BuildBias(market, today, $"Yahoo:{market.YahooSymbol}", candles);
                    _context.MarketBiases.Add(bias);
                    await _context.SaveChangesAsync(cancellationToken);
                    return bias;
                }
                catch (DbUpdateException)
                {
                    _context.ChangeTracker.Clear();
                    return await _context.MarketBiases
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Symbol == market.Symbol && b.BiasDate == today, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not refresh daily market bias for {Symbol} using Yahoo {SourceSymbol}.", market.Symbol, market.YahooSymbol);
                    break;
                }
            }

            foreach (var stooqSymbol in market.StooqSymbols)
            {
                try
                {
                    var candles = await FetchStooqDailyCandlesAsync(stooqSymbol, cancellationToken);
                    if (candles.Count < 55)
                        continue;

                    var bias = BuildBias(market, today, $"Stooq:{stooqSymbol}", candles);
                    _context.MarketBiases.Add(bias);
                    await _context.SaveChangesAsync(cancellationToken);
                    return bias;
                }
                catch (DbUpdateException)
                {
                    _context.ChangeTracker.Clear();
                    return await _context.MarketBiases
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Symbol == market.Symbol && b.BiasDate == today, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not refresh daily market bias for {Symbol} using Stooq {SourceSymbol}.", market.Symbol, stooqSymbol);
                }
            }

            return null;
        }

        private async Task<List<DailyCandle>> FetchYahooDailyCandlesAsync(string symbol, CancellationToken cancellationToken)
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=6mo&interval=1d";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeDesk/1.0");

            var json = await client.GetStringAsync(url, cancellationToken);
            using var document = JsonDocument.Parse(json);

            var result = document.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0];

            var timestamps = result.GetProperty("timestamp").EnumerateArray().ToList();
            var quote = result
                .GetProperty("indicators")
                .GetProperty("quote")[0];

            var opens = quote.GetProperty("open").EnumerateArray().ToList();
            var highs = quote.GetProperty("high").EnumerateArray().ToList();
            var lows = quote.GetProperty("low").EnumerateArray().ToList();
            var closes = quote.GetProperty("close").EnumerateArray().ToList();
            var candles = new List<DailyCandle>();

            for (var i = 0; i < timestamps.Count; i++)
            {
                if (opens[i].ValueKind == JsonValueKind.Null ||
                    highs[i].ValueKind == JsonValueKind.Null ||
                    lows[i].ValueKind == JsonValueKind.Null ||
                    closes[i].ValueKind == JsonValueKind.Null)
                    continue;

                var date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime.Date;
                candles.Add(new DailyCandle(
                    date,
                    Convert.ToDecimal(opens[i].GetDouble(), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(highs[i].GetDouble(), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(lows[i].GetDouble(), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(closes[i].GetDouble(), CultureInfo.InvariantCulture)));
            }

            return candles
                .OrderBy(c => c.Date)
                .ToList();
        }

        private async Task<List<DailyCandle>> FetchStooqDailyCandlesAsync(string symbol, CancellationToken cancellationToken)
        {
            var from = DateTime.UtcNow.Date.AddDays(-170).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var to = DateTime.UtcNow.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var url = $"https://stooq.com/q/d/l/?s={Uri.EscapeDataString(symbol)}&d1={from}&d2={to}&i=d";

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeDesk/1.0");

            var csv = await client.GetStringAsync(url, cancellationToken);
            return ParseCsv(csv);
        }

        private static List<DailyCandle> ParseCsv(string csv)
        {
            var candles = new List<DailyCandle>();
            var rows = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            foreach (var row in rows.Skip(1))
            {
                var parts = row.Split(',');
                if (parts.Length < 5 || parts.Any(part => part.Equals("N/D", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var open) ||
                    !decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var high) ||
                    !decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var low) ||
                    !decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                    continue;

                candles.Add(new DailyCandle(date, open, high, low, close));
            }

            return candles
                .OrderBy(c => c.Date)
                .ToList();
        }

        private static MarketBias BuildBias(MarketDefinition market, DateTime today, string sourceSymbol, IReadOnlyList<DailyCandle> candles)
        {
            var last = candles[^1];
            var previous = candles[^2];
            var closes = candles.Select(c => c.Close).ToList();
            var ma20 = closes.TakeLast(20).Average();
            var ma50 = closes.TakeLast(50).Average();
            var rsi = CalculateRsi(closes, 14);
            var dayChange = previous.Close == 0 ? 0 : (last.Close - previous.Close) / previous.Close * 100m;

            var score = 0;
            score += last.Close > ma20 ? 1 : -1;
            score += last.Close > ma50 ? 1 : -1;
            score += ma20 > ma50 ? 1 : -1;
            score += rsi > 58 ? 1 : rsi < 42 ? -1 : 0;
            score += last.Close > previous.Close ? 1 : -1;

            var direction = score >= 2 ? "Bullish" : score <= -2 ? "Bearish" : "Neutral";
            var stars = Math.Clamp(Math.Abs(score), 1, 5);
            var bullets = BuildBullets(last.Close, ma20, ma50, rsi, dayChange, direction);

            return new MarketBias
            {
                Symbol = market.Symbol,
                DisplayName = market.DisplayName,
                BiasDate = today,
                Direction = direction,
                SignalScore = score,
                ConfidenceStars = stars,
                LastClose = last.Close,
                ChangePercent = Math.Round(dayChange, 2),
                Rsi14 = Math.Round(rsi, 1),
                Ma20 = ma20,
                Ma50 = ma50,
                Summary = BuildSummary(market.DisplayName, direction, score),
                BulletJson = JsonSerializer.Serialize(bullets),
                Source = sourceSymbol,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static decimal CalculateRsi(IReadOnlyList<decimal> closes, int period)
        {
            if (closes.Count <= period)
                return 50m;

            var recent = closes.TakeLast(period + 1).ToList();
            var gains = 0m;
            var losses = 0m;

            for (var i = 1; i < recent.Count; i++)
            {
                var change = recent[i] - recent[i - 1];
                if (change >= 0)
                    gains += change;
                else
                    losses += Math.Abs(change);
            }

            var averageGain = gains / period;
            var averageLoss = losses / period;

            if (averageLoss == 0)
                return 100m;

            var rs = averageGain / averageLoss;
            return 100m - (100m / (1m + rs));
        }

        private static IReadOnlyList<string> BuildBullets(decimal close, decimal ma20, decimal ma50, decimal rsi, decimal dayChange, string direction)
        {
            var trend = close > ma20 && close > ma50
                ? "Cena je nad MA20 i MA50, trend má zatím kupující strukturu."
                : close < ma20 && close < ma50
                    ? "Cena je pod MA20 i MA50, trh je technicky slabší."
                    : "Cena je mezi MA20 a MA50, bias je smíšený.";

            var momentum = rsi >= 60
                ? $"RSI {rsi:0.#} ukazuje silnější momentum."
                : rsi <= 40
                    ? $"RSI {rsi:0.#} ukazuje slabší momentum."
                    : $"RSI {rsi:0.#} je ve středním pásmu.";

            var change = dayChange >= 0
                ? $"Poslední den uzavřel +{dayChange:0.##}%."
                : $"Poslední den uzavřel {dayChange:0.##}%.";

            return
            [
                trend,
                momentum,
                change,
                direction == "Neutral"
                    ? "Pro dnešek má smysl čekat na potvrzení směru."
                    : "Bias ber jako kontext, ne jako samostatný vstup do obchodu."
            ];
        }

        private static string BuildSummary(string displayName, string direction, int score)
        {
            return direction switch
            {
                "Bullish" => $"{displayName} má pro dnešek spíš býčí technický bias.",
                "Bearish" => $"{displayName} má pro dnešek spíš medvědí technický bias.",
                _ => $"{displayName} je pro dnešek bez jasné technické převahy."
            } + $" Signální skóre: {score:+#;-#;0}.";
        }

        private static MarketBias CreateUnavailableBias(MarketDefinition market, DateTime today)
        {
            return new MarketBias
            {
                Symbol = market.Symbol,
                DisplayName = market.DisplayName,
                BiasDate = today,
                Direction = "Neutral",
                SignalScore = 0,
                ConfidenceStars = 1,
                Summary = "Denní bias zatím není dostupný. Zkus stránku obnovit později.",
                BulletJson = JsonSerializer.Serialize(new[]
                {
                    "Externí data se nepodařilo načíst.",
                    "Aplikace nespadla a použila bezpečný neutrální fallback.",
                    "Pro obchodování použij vlastní analýzu grafu."
                }),
                Source = "Fallback",
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static MarketBiasCard ToCard(MarketBias bias)
        {
            IReadOnlyList<string> bullets;
            try
            {
                bullets = JsonSerializer.Deserialize<IReadOnlyList<string>>(bias.BulletJson) ?? [];
            }
            catch
            {
                bullets = [];
            }

            return new MarketBiasCard
            {
                Symbol = bias.Symbol,
                DisplayName = bias.DisplayName,
                Direction = bias.Direction,
                SignalScore = bias.SignalScore,
                ConfidenceStars = bias.ConfidenceStars,
                LastClose = bias.LastClose,
                ChangePercent = bias.ChangePercent,
                Rsi14 = bias.Rsi14,
                Summary = bias.Summary,
                Bullets = bullets,
                Source = bias.Source
            };
        }

        private sealed record MarketDefinition(string Symbol, string DisplayName, string YahooSymbol, string[] StooqSymbols);
        private sealed record DailyCandle(DateTime Date, decimal Open, decimal High, decimal Low, decimal Close);
    }
}
