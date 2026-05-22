using System.Globalization;
using System.Text.Json;
using TradingJournal.ViewModels;

namespace TradingJournal.Services
{
    public class HomeInsightService
    {
        private static readonly HashSet<string> ImportantCurrencies = new(StringComparer.OrdinalIgnoreCase)
        {
            "USD",
            "EUR",
            "GBP"
        };

        private static readonly string[] HighImpactKeywords =
        [
            "cpi",
            "ppi",
            "payroll",
            "nfp",
            "unemployment",
            "jobless",
            "gdp",
            "retail sales",
            "fomc",
            "federal funds",
            "interest rate",
            "powell",
            "fed"
        ];

        private static readonly string[] MediumImpactKeywords =
        [
            "pmi",
            "ism",
            "jolts",
            "durable",
            "consumer confidence",
            "housing",
            "industrial production",
            "balance of trade",
            "speech"
        ];

        private readonly MarketBiasService _marketBiasService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HomeInsightService> _logger;
        private readonly AppClock _clock;

        public HomeInsightService(
            MarketBiasService marketBiasService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<HomeInsightService> logger,
            AppClock clock)
        {
            _marketBiasService = marketBiasService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _clock = clock;
        }

        public async Task<MarketRegimeCard> GetMarketRegimeAsync(CancellationToken cancellationToken = default)
        {
            var model = await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken);
            var nq = model.MarketBiases.FirstOrDefault(b => b.Symbol == "NQ");
            var btc = model.MarketBiases.FirstOrDefault(b => b.Symbol == "BTC");
            var xau = model.MarketBiases.FirstOrDefault(b => b.Symbol == "XAU");

            var score = 0;
            score += NormalizeRiskScore(nq?.SignalScore);
            score += NormalizeRiskScore(btc?.SignalScore);
            score -= NormalizeRiskScore(xau?.SignalScore) / 2;

            var regime = score >= 2 ? "Risk-on" : score <= -2 ? "Risk-off" : "Mixed";
            var tone = score >= 2 ? "bullish" : score <= -2 ? "bearish" : "neutral";
            var summary = regime switch
            {
                "Risk-on" => "Rizikova aktiva maji lepsi technicky kontext. Long setupy na NQ/BTC maji vetsi podporu trhu.",
                "Risk-off" => "Trh pusobi defenzivne. Opatrne s agresivnimi longy na indexech a kryptu.",
                _ => "Kontext je smiseny. Vetsi smysl dava cekat na potvrzeni a netlacit obchod uprostred range."
            };

            var drivers = new[]
            {
                BuildDriver("NQ", nq),
                BuildDriver("BTC", btc),
                BuildDriver("XAU", xau)
            };

            return new MarketRegimeCard
            {
                Regime = regime,
                Tone = tone,
                Score = score,
                Summary = summary,
                Drivers = drivers
            };
        }

        public async Task<IReadOnlyList<EconomicEventCard>> GetEconomicCalendarAsync(CancellationToken cancellationToken = default)
        {
            var apiKey = _configuration["FinancialModelingPrep:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                return GetFallbackEconomicCalendar();

            try
            {
                var today = _clock.Now.Date;
                var url = "https://financialmodelingprep.com/stable/economic-calendar" +
                    $"?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&apikey={Uri.EscapeDataString(apiKey)}";

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Economic calendar API returned {StatusCode}.", response.StatusCode);
                    return GetFallbackEconomicCalendar();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return GetFallbackEconomicCalendar();

                var events = document.RootElement
                    .EnumerateArray()
                    .Select(ParseEconomicEvent)
                    .Where(e => e is not null)
                    .Select(e => e!)
                    .Where(IsRelevantEconomicEvent)
                    .OrderByDescending(e => ImpactWeight(e.Impact))
                    .ThenBy(e => e.Time)
                    .Take(8)
                    .ToList();

                return events.Count > 0
                    ? events
                    : GetFallbackEconomicCalendar();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Could not load economic calendar API.");
                return GetFallbackEconomicCalendar();
            }
        }

        private IReadOnlyList<EconomicEventCard> GetFallbackEconomicCalendar()
        {
            var now = _clock.Now;
            var events = new List<EconomicEventCard>
            {
                new()
                {
                    Time = "14:30",
                    Title = "US macro okno",
                    Currency = "USD",
                    Impact = "High",
                    Source = "Fallback",
                    Note = "Zkontroluj, jestli nevychazi CPI, PPI, NFP nebo jobless claims. Tyhle zpravy hybou XAU, NQ i USD."
                },
                new()
                {
                    Time = "15:30",
                    Title = "US cash open",
                    Currency = "USD",
                    Impact = "Medium",
                    Source = "Fallback",
                    Note = "NQ miva prudsi likviditu a falesne breaky. Prvnich 5-15 minut ber opatrne."
                },
                new()
                {
                    Time = "20:00",
                    Title = "Fed watch",
                    Currency = "USD",
                    Impact = "Medium",
                    Source = "Fallback",
                    Note = "U zlata a indexu hlidej FOMC minutes, sazby, projevy Fedu a pohyb US10Y/DXY."
                }
            };

            if (now.DayOfWeek == DayOfWeek.Friday)
            {
                events.Insert(0, new EconomicEventCard
                {
                    Time = "14:30",
                    Title = "Patek: payrolls / employment watch",
                    Currency = "USD",
                    Impact = "High",
                    Source = "Fallback",
                    Note = "Prvni patek v mesici byva NFP. Pokud je dnes NFP den, zlato i NQ mohou byt vyrazne volatilni."
                });
            }

            if (now.DayOfWeek == DayOfWeek.Thursday)
            {
                events.Insert(0, new EconomicEventCard
                {
                    Time = "14:30",
                    Title = "Ctvrtek: jobless claims watch",
                    Currency = "USD",
                    Impact = "Medium",
                    Source = "Fallback",
                    Note = "Zkontroluj, jestli dnes vychazi zadosti o podporu. Casto hybou USD, zlatem i indexy."
                });
            }

            return events
                .Take(4)
                .ToList();
        }

        public IReadOnlyList<TradeSetupCard> GetSetupWatchlist()
        {
            return
            [
                new()
                {
                    Symbol = "XAU",
                    Bias = "Wait for level",
                    Trigger = "Sweep liquidity nebo retest silne S/R zony, ne honit impuls.",
                    Invalidation = "Zrusit napad, kdyz DXY a yields jdou ostre proti setupu.",
                    Focus = "London/NY overlap, reakce na US macro."
                },
                new()
                {
                    Symbol = "NQ",
                    Bias = "Confirmation",
                    Trigger = "Break + retest OR sweep high/low a rychly reclaim.",
                    Invalidation = "Nepokracovat po prvnim failed retestu nebo pred high-impact daty.",
                    Focus = "US open, trend day vs range day."
                },
                new()
                {
                    Symbol = "BTC",
                    Bias = "Range awareness",
                    Trigger = "Obchodovat kraje range nebo potvrzeny breakout s volume.",
                    Invalidation = "Netredovat stred range bez jasneho R/R.",
                    Focus = "Reakce na risk sentiment a technologicke akcie."
                }
            ];
        }

        private static int NormalizeRiskScore(int? score)
        {
            return score switch
            {
                >= 2 => 1,
                <= -2 => -1,
                _ => 0
            };
        }

        private static string BuildDriver(string symbol, MarketBiasCard? bias)
        {
            if (bias is null)
                return $"{symbol}: data nejsou dostupna";

            return $"{symbol}: {bias.Direction} ({bias.SignalScore:+#;-#;0})";
        }

        private EconomicEventCard? ParseEconomicEvent(JsonElement item)
        {
            var title = ReadString(item, "event") ?? ReadString(item, "title") ?? ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(title))
                return null;

            var currency = ReadString(item, "currency") ?? ReadString(item, "country") ?? string.Empty;
            var impact = NormalizeImpact(ReadString(item, "impact"), title);
            var dateValue = ReadString(item, "date") ?? ReadString(item, "datetime");
            var time = FormatEconomicEventTime(dateValue);
            var actual = ReadDisplayValue(item, "actual");
            var forecast = ReadDisplayValue(item, "estimate") ?? ReadDisplayValue(item, "forecast");
            var previous = ReadDisplayValue(item, "previous");

            return new EconomicEventCard
            {
                Time = time,
                Title = title.Trim(),
                Currency = currency.Trim().ToUpperInvariant(),
                Impact = impact,
                Actual = actual,
                Forecast = forecast,
                Previous = previous,
                Source = "Financial Modeling Prep",
                Note = BuildEconomicNote(currency, actual, forecast, previous)
            };
        }

        private static bool IsRelevantEconomicEvent(EconomicEventCard item)
        {
            if (string.IsNullOrWhiteSpace(item.Currency))
                return ImpactWeight(item.Impact) >= 2;

            return ImportantCurrencies.Contains(item.Currency) && ImpactWeight(item.Impact) >= 2;
        }

        private static string BuildEconomicNote(string currency, string? actual, string? forecast, string? previous)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(actual))
                parts.Add($"actual {actual}");

            if (!string.IsNullOrWhiteSpace(forecast))
                parts.Add($"forecast {forecast}");

            if (!string.IsNullOrWhiteSpace(previous))
                parts.Add($"previous {previous}");

            var prefix = string.IsNullOrWhiteSpace(currency) ? "Makro udalost" : $"{currency.ToUpperInvariant()} udalost";
            return parts.Count > 0
                ? $"{prefix}: {string.Join(", ", parts)}."
                : $"{prefix}: hlidej reakci na XAU, NQ, DXY a yields.";
        }

        private static string NormalizeImpact(string? impact, string title)
        {
            if (!string.IsNullOrWhiteSpace(impact))
            {
                if (impact.Contains("high", StringComparison.OrdinalIgnoreCase))
                    return "High";

                if (impact.Contains("medium", StringComparison.OrdinalIgnoreCase))
                    return "Medium";

                if (impact.Contains("low", StringComparison.OrdinalIgnoreCase))
                    return "Low";
            }

            return GetKeywordImpact(title);
        }

        private static string GetKeywordImpact(string title)
        {
            var normalized = title.ToLowerInvariant();

            if (HighImpactKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return "High";

            if (MediumImpactKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return "Medium";

            return "Low";
        }

        private static int ImpactWeight(string impact)
        {
            return impact switch
            {
                "High" => 3,
                "Medium" => 2,
                _ => 1
            };
        }

        private string FormatEconomicEventTime(string? dateValue)
        {
            if (string.IsNullOrWhiteSpace(dateValue))
                return "--:--";

            if (DateTimeOffset.TryParse(
                dateValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                return _clock.FormatTime(parsed);
            }

            return dateValue.Length >= 16 ? dateValue.Substring(11, 5) : "--:--";
        }

        private static string? ReadDisplayValue(JsonElement item, string propertyName)
        {
            var value = ReadProperty(item, propertyName);
            if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;

            return value.Value.ValueKind switch
            {
                JsonValueKind.Number => value.Value.TryGetDecimal(out var number)
                    ? number.ToString("0.##", CultureInfo.InvariantCulture)
                    : value.Value.GetRawText(),
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.Value.GetString()) ? null : value.Value.GetString(),
                _ => value.Value.GetRawText()
            };
        }

        private static string? ReadString(JsonElement item, string propertyName)
        {
            var value = ReadProperty(item, propertyName);
            if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;

            return value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString()
                : value.Value.ToString();
        }

        private static JsonElement? ReadProperty(JsonElement item, string propertyName)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            }

            return null;
        }
    }
}
