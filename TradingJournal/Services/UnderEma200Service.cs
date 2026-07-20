using System.Globalization;
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
        private readonly ILogger<UnderEma200Service> _logger;

        public UnderEma200Service(IHttpClientFactory httpClientFactory, ILogger<UnderEma200Service> logger)
        {
            _httpClientFactory = httpClientFactory;
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

                var tasks = Candidates.Select(candidate => AnalyzeCandidateAsync(client, candidate, cancellationToken));
                var cards = (await Task.WhenAll(tasks))
                    .Where(card => card is not null)
                    .Select(card => card!)
                    .OrderByDescending(card => card.Score)
                    .ThenBy(card => card.DiscountToEmaPercent)
                    .ToList();

                var result = new UnderEma200Result
                {
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Items = cards,
                    Message = cards.Count == 0
                        ? "Nenalezeny zadne akcie z watchlistu, ktere jsou pod weekly EMA200 a zaroven maji rostouci dlouhodoby trend."
                        : "Screening hleda rustove weekly grafy, kde je cena aktualne pod EMA200."
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
                var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={candidate.Symbol.ToLowerInvariant()}.us&i=d", cancellationToken);
                var candles = ParseDailyCandles(csv);
                if (candles.Count < 1100)
                {
                    return null;
                }

                var weeklyCloses = candles
                    .GroupBy(candle => $"{ISOWeek.GetYear(candle.Date)}-{ISOWeek.GetWeekOfYear(candle.Date):00}")
                    .Select(group => group.OrderBy(candle => candle.Date).Last())
                    .OrderBy(candle => candle.Date)
                    .ToList();

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

                if (discountToEma >= 0 || emaSlope52 <= 0 || return5Year < 20)
                {
                    return null;
                }

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
                    LastPriceDate = latest.Date,
                    DataSource = "Stooq EOD",
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
            return $"Cena je {Math.Abs(discountToEma).ToString("0.#", CultureInfo.InvariantCulture)} % pod weekly EMA200, EMA200 za 52 tydnu roste o {emaSlope52.ToString("0.#", CultureInfo.InvariantCulture)} % a 5lety vykon je {return5Year.ToString("0.#", CultureInfo.InvariantCulture)} %. Nad EMA200 byla akcie {aboveEmaShare.ToString("0.#", CultureInfo.InvariantCulture)} % poslednich 104 tydnu.";
        }

        private sealed record StockCandidate(string Symbol, string Name, string Sector);
        private sealed record DailyCandle(DateTime Date, decimal Close);
    }
}
