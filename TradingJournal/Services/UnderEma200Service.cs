using System.Globalization;
using System.Text.Json;
using TradingJournal.ViewModels;

namespace TradingJournal.Services
{
    public class UnderEma200Service
    {
        private const int EmaPeriod = 200;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(4);
        private static readonly SemaphoreSlim CacheLock = new(1, 1);
        private static UnderEma200Result? CachedResult;
        private static DateTimeOffset CachedAt;

        private static readonly StockCandidate[] Candidates =
        [
            new("MCD", "McDonald's Corporation", "Consumer Defensive"),
            new("MSFT", "Microsoft Corporation", "Technology"),
            new("AAPL", "Apple Inc.", "Technology"),
            new("GOOGL", "Alphabet Inc.", "Communication Services"),
            new("AMZN", "Amazon.com Inc.", "Consumer Cyclical"),
            new("META", "Meta Platforms Inc.", "Communication Services"),
            new("NVDA", "NVIDIA Corporation", "Semiconductors"),
            new("AVGO", "Broadcom Inc.", "Semiconductors"),
            new("TSM", "Taiwan Semiconductor", "Semiconductors"),
            new("ASML", "ASML Holding N.V.", "Semiconductors"),
            new("COST", "Costco Wholesale", "Consumer Defensive"),
            new("V", "Visa Inc.", "Financial Services"),
            new("MA", "Mastercard Incorporated", "Financial Services"),
            new("HD", "Home Depot Inc.", "Consumer Cyclical"),
            new("LOW", "Lowe's Companies", "Consumer Cyclical"),
            new("UNH", "UnitedHealth Group", "Healthcare"),
            new("LLY", "Eli Lilly and Company", "Healthcare"),
            new("NKE", "Nike Inc.", "Consumer Cyclical"),
            new("SBUX", "Starbucks Corporation", "Consumer Cyclical"),
            new("CRM", "Salesforce Inc.", "Technology"),
            new("ADBE", "Adobe Inc.", "Technology"),
            new("INTU", "Intuit Inc.", "Technology"),
            new("ISRG", "Intuitive Surgical", "Healthcare"),
            new("SPGI", "S&P Global Inc.", "Financial Services"),
            new("LIN", "Linde plc", "Basic Materials")
        ];

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UnderEma200Service> _logger;

        public UnderEma200Service(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<UnderEma200Service> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<UnderEma200Result> GetAsync(CancellationToken cancellationToken = default)
        {
            if (CachedResult is not null && DateTimeOffset.UtcNow - CachedAt < CacheLifetime)
            {
                return CachedResult;
            }

            await CacheLock.WaitAsync(cancellationToken);
            try
            {
                if (CachedResult is not null && DateTimeOffset.UtcNow - CachedAt < CacheLifetime)
                {
                    return CachedResult;
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeDesk/1.0");

                using var throttle = new SemaphoreSlim(5, 5);
                var tasks = Candidates.Select(async candidate =>
                {
                    await throttle.WaitAsync(cancellationToken);
                    try
                    {
                        return await AnalyzeCandidateAsync(client, candidate, cancellationToken);
                    }
                    finally
                    {
                        throttle.Release();
                    }
                });

                var analyzedCards = (await Task.WhenAll(tasks))
                    .Where(card => card is not null)
                    .Select(card => card!)
                    .ToList();

                var strictCards = analyzedCards
                    .Where(card => card.MatchesTrendPullback)
                    .OrderByDescending(card => card.Score)
                    .ThenBy(card => card.DiscountToEmaPercent)
                    .ToList();

                var cards = strictCards;

                var result = new UnderEma200Result
                {
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Items = cards,
                    Message = strictCards.Count > 0
                        ? $"Nalezeno {strictCards.Count} akcii z watchlistu pod rostouci weekly EMA200."
                        : analyzedCards.Count > 0
                            ? "Z watchlistu ted zadna akcie nesplnuje podminku: cena je pod rostouci weekly EMA200 a dlouhodoby trend zustava rustovy."
                            : "Nepodarilo se nacist cenova data z provideru Stooq EOD."
                };

                CachedResult = result;
                CachedAt = DateTimeOffset.UtcNow;

                return result;
            }
            finally
            {
                CacheLock.Release();
            }
        }

        private async Task<UnderEma200StockCard?> AnalyzeCandidateAsync(HttpClient client, StockCandidate candidate, CancellationToken cancellationToken)
        {
            try
            {
                var dataSource = "Stooq EOD";
                var candles = await FetchStooqDailyCandlesAsync(client, candidate.Symbol, cancellationToken);

                if (candles.Count < 1100)
                {
                    var alphaCandles = await FetchAlphaVantageWeeklyCandlesAsync(client, candidate.Symbol, cancellationToken);
                    if (alphaCandles.Count > 0)
                    {
                        candles = alphaCandles;
                        dataSource = "Alpha Vantage weekly";
                    }
                }

                if (candles.Count < EmaPeriod + 60)
                {
                    return null;
                }

                var weeklyCloses = candles.Count >= 900
                    ? candles
                        .GroupBy(candle => $"{ISOWeek.GetYear(candle.Date)}-{ISOWeek.GetWeekOfYear(candle.Date):00}")
                        .Select(group => group.OrderBy(candle => candle.Date).Last())
                        .OrderBy(candle => candle.Date)
                        .ToList()
                    : candles.OrderBy(candle => candle.Date).ToList();

                if (weeklyCloses.Count < EmaPeriod + 60)
                {
                    return null;
                }

                var ema = CalculateEma(weeklyCloses.Select(candle => candle.Close).ToList(), EmaPeriod);
                var latest = weeklyCloses[^1];
                var latestEma = ema[^1];
                var ema52WeeksAgo = ema[^53];
                var closeFiveYearsAgo = weeklyCloses[^Math.Min(261, weeklyCloses.Count)].Close;

                if (latestEma <= 0 || closeFiveYearsAgo <= 0)
                {
                    return null;
                }

                var discountToEma = (latest.Close - latestEma) / latestEma * 100m;
                var emaSlope52 = (latestEma - ema52WeeksAgo) / ema52WeeksAgo * 100m;
                var return5Year = (latest.Close - closeFiveYearsAgo) / closeFiveYearsAgo * 100m;
                var aboveEmaShare = GetAboveEmaShare(weeklyCloses, ema, 104);

                var isUnderEma = discountToEma < 0;
                var hasRisingEma = emaSlope52 > 0;
                var hasLongTermUptrend = return5Year > 0 || aboveEmaShare >= 55;
                var matchesTrendPullback = isUnderEma && hasRisingEma && hasLongTermUptrend;

                var score = Score(discountToEma, emaSlope52, return5Year, aboveEmaShare);

                return new UnderEma200StockCard
                {
                    Symbol = candidate.Symbol,
                    Name = candidate.Name,
                    Sector = candidate.Sector,
                    Price = latest.Close,
                    WeeklyEma200 = latestEma,
                    DiscountToEmaPercent = discountToEma,
                    EmaSlope52WeekPercent = emaSlope52,
                    Return5YearPercent = return5Year,
                    WeeksAboveEmaSharePercent = aboveEmaShare,
                    Score = score,
                    IsUnderEma200 = isUnderEma,
                    HasRisingEma200 = hasRisingEma,
                    MatchesTrendPullback = matchesTrendPullback,
                    LastPriceDate = latest.Date,
                    DataSource = dataSource,
                    ChartUrl = $"https://www.tradingview.com/chart/?symbol={candidate.Symbol}",
                    Rationale = BuildRationale(discountToEma, emaSlope52, return5Year, aboveEmaShare)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not analyze {Symbol} for weekly EMA200 screen.", candidate.Symbol);
                return null;
            }
        }

        private static async Task<List<DailyCandle>> FetchStooqDailyCandlesAsync(HttpClient client, string symbol, CancellationToken cancellationToken)
        {
            var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={symbol.ToLowerInvariant()}.us&i=d", cancellationToken);
            return ParseDailyCandles(csv);
        }

        private async Task<List<DailyCandle>> FetchAlphaVantageWeeklyCandlesAsync(HttpClient client, string symbol, CancellationToken cancellationToken)
        {
            var apiKey = _configuration["AlphaVantage:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return [];
            }

            var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_WEEKLY_ADJUSTED&symbol={Uri.EscapeDataString(symbol)}&apikey={Uri.EscapeDataString(apiKey)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("Weekly Adjusted Time Series", out var timeSeries))
            {
                return [];
            }

            var candles = new List<DailyCandle>();
            foreach (var week in timeSeries.EnumerateObject())
            {
                if (!DateTime.TryParseExact(week.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (!TryReadAlphaDecimal(week.Value, "5. adjusted close", out var close) &&
                    !TryReadAlphaDecimal(week.Value, "4. close", out close))
                {
                    continue;
                }

                candles.Add(new DailyCandle(date, close));
            }

            return candles.OrderBy(candle => candle.Date).ToList();
        }

        private static List<DailyCandle> ParseDailyCandles(string csv)
        {
            var candles = new List<DailyCandle>();
            foreach (var line in csv.Split('\n').Skip(1))
            {
                var parts = line.Trim().Split(',');
                if (parts.Length < 5 ||
                    !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    !decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                {
                    continue;
                }

                candles.Add(new DailyCandle(date, close));
            }

            return candles;
        }

        private static bool TryReadAlphaDecimal(JsonElement source, string propertyName, out decimal value)
        {
            value = 0;
            return source.TryGetProperty(propertyName, out var property) &&
                decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static List<decimal> CalculateEma(IReadOnlyList<decimal> values, int period)
        {
            var ema = new List<decimal>(values.Count);
            var multiplier = 2m / (period + 1);
            var seed = values.Take(period).Average();

            for (var i = 0; i < values.Count; i++)
            {
                if (i < period - 1)
                {
                    ema.Add(values.Take(i + 1).Average());
                    continue;
                }

                if (i == period - 1)
                {
                    ema.Add(seed);
                    continue;
                }

                ema.Add((values[i] - ema[i - 1]) * multiplier + ema[i - 1]);
            }

            return ema;
        }

        private static decimal GetAboveEmaShare(IReadOnlyList<DailyCandle> weeklyCloses, IReadOnlyList<decimal> ema, int lookback)
        {
            var start = Math.Max(0, weeklyCloses.Count - lookback);
            var total = weeklyCloses.Count - start;
            if (total <= 0)
            {
                return 0;
            }

            var above = 0;
            for (var i = start; i < weeklyCloses.Count; i++)
            {
                if (weeklyCloses[i].Close > ema[i])
                {
                    above++;
                }
            }

            return above * 100m / total;
        }

        private static int Score(decimal discountToEma, decimal emaSlope52, decimal return5Year, decimal aboveEmaShare)
        {
            var score = 35;
            score += emaSlope52 switch
            {
                >= 12 => 20,
                >= 7 => 16,
                >= 3 => 11,
                _ => 6
            };
            score += return5Year switch
            {
                >= 150 => 20,
                >= 90 => 16,
                >= 50 => 11,
                _ => 6
            };
            score += aboveEmaShare switch
            {
                >= 75 => 15,
                >= 60 => 11,
                >= 45 => 7,
                _ => 3
            };
            score += Math.Abs(discountToEma) switch
            {
                >= 5 and <= 25 => 10,
                > 25 and <= 40 => 5,
                > 40 => -8,
                _ => 4
            };

            return Math.Clamp(score, 0, 100);
        }

        private static string BuildRationale(decimal discountToEma, decimal emaSlope52, decimal return5Year, decimal aboveEmaShare)
        {
            var emaDistanceText = discountToEma < 0
                ? $"Cena je {Math.Abs(discountToEma).ToString("0.#", CultureInfo.InvariantCulture)} % pod weekly EMA200"
                : $"Cena je {discountToEma.ToString("0.#", CultureInfo.InvariantCulture)} % nad weekly EMA200";

            return $"{emaDistanceText}, EMA200 za 52 tydnu roste o {emaSlope52.ToString("0.#", CultureInfo.InvariantCulture)} % a 5lety vykon je {return5Year.ToString("0.#", CultureInfo.InvariantCulture)} %. Nad EMA200 byla akcie {aboveEmaShare.ToString("0.#", CultureInfo.InvariantCulture)} % poslednich 104 tydnu.";
        }

        private sealed record StockCandidate(string Symbol, string Name, string Sector);
        private sealed record DailyCandle(DateTime Date, decimal Close);
    }
}
