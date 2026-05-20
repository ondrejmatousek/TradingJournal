using System.Globalization;
using System.Text.Json;
using TradingJournal.ViewModels;

namespace TradingJournal.Services
{
    public class StockRadarService
    {
        private static readonly string[] Symbols =
        [
            "MSFT", "GOOGL", "AMZN", "AAPL", "META", "NVDA", "AMD", "ASML", "TSM", "AVGO",
            "CRM", "ADBE", "NOW", "SNOW", "PANW", "CRWD", "SHOP", "UBER", "INTC", "QCOM"
        ];

        private static readonly IReadOnlyDictionary<string, string> StockNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MSFT"] = "Microsoft Corporation",
                ["GOOGL"] = "Alphabet Inc.",
                ["AMZN"] = "Amazon.com Inc.",
                ["AAPL"] = "Apple Inc.",
                ["META"] = "Meta Platforms Inc.",
                ["NVDA"] = "NVIDIA Corporation",
                ["AMD"] = "Advanced Micro Devices Inc.",
                ["ASML"] = "ASML Holding N.V.",
                ["TSM"] = "Taiwan Semiconductor",
                ["AVGO"] = "Broadcom Inc.",
                ["CRM"] = "Salesforce Inc.",
                ["ADBE"] = "Adobe Inc.",
                ["NOW"] = "ServiceNow Inc.",
                ["SNOW"] = "Snowflake Inc.",
                ["PANW"] = "Palo Alto Networks",
                ["CRWD"] = "CrowdStrike Holdings",
                ["SHOP"] = "Shopify Inc.",
                ["UBER"] = "Uber Technologies",
                ["INTC"] = "Intel Corporation",
                ["QCOM"] = "Qualcomm Inc."
            };

        private static readonly IReadOnlyDictionary<string, StockFallback> FallbackIdeas =
            new Dictionary<string, StockFallback>(StringComparer.OrdinalIgnoreCase)
            {
                ["MSFT"] = new("Microsoft Corporation", "Software", 6, 31.5m, 2.1m, 14.2m, 476m, 555m, "Kvalitni software business s vysokymi marzemi a silnym cloud segmentem."),
                ["GOOGL"] = new("Alphabet Inc.", "Communication Services", 6, 22.8m, 1.4m, 13.1m, 173m, 207m, "Silny cashflow, vyhledavani a AI infrastruktura; valuace byva rozumnejsi nez u casti megacapu."),
                ["AMZN"] = new("Amazon.com Inc.", "Consumer Cyclical", 5, 35.2m, 1.9m, 11.8m, 186m, 242m, "AWS a reklama mohou dlouhodobe tahnout marze, retail zustava objemova zakladna."),
                ["AAPL"] = new("Apple Inc.", "Technology", 4, 29.4m, 2.5m, 6.3m, 197m, 260m, "Vysoce kvalitni firma, ale pro watchlist dava smysl hlidat hlavne cenu proti rustu."),
                ["META"] = new("Meta Platforms Inc.", "Communication Services", 6, 24.6m, 1.2m, 15.5m, 626m, 740m, "Silna profitabilita reklamniho businessu a opce na AI/Reality Labs."),
                ["NVDA"] = new("NVIDIA Corporation", "Semiconductors", 5, 38.7m, 1.6m, 35.0m, 135m, 195m, "Leader v AI akceleratorech; kvalita je vysoka, riziko je hlavne v oceneni."),
                ["AMD"] = new("Advanced Micro Devices Inc.", "Semiconductors", 5, 33.0m, 1.7m, 18.0m, 164m, 227m, "AI a serverove CPU mohou podporit rust, ale cyklicnost polovodicu zustava riziko."),
                ["ASML"] = new("ASML Holding N.V.", "Semiconductors", 6, 29.8m, 1.5m, 10.4m, 755m, 1110m, "Unikatni pozice v litografii a dlouhodoba poptavka po pokrocile vyrobe cipu."),
                ["TSM"] = new("Taiwan Semiconductor", "Semiconductors", 6, 21.7m, 1.1m, 17.2m, 238m, 286m, "Klicovy foundry hrac pro AI a high-end cipy, solidni pomer rustu a valuace."),
                ["AVGO"] = new("Broadcom Inc.", "Semiconductors", 5, 32.2m, 1.8m, 12.0m, 278m, 386m, "Kombinace polovodicu a infrastrukturalniho software, zajimavy cashflow profil.")
            };

        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StockRadarService> _logger;

        public StockRadarService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<StockRadarService> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<StockRadarResult> GetStockRadarAsync(CancellationToken cancellationToken = default)
        {
            var apiKey = _configuration["AlphaVantage:ApiKey"];
            var ideas = new List<StockIdeaCard>();

            try
            {
                ideas = await FetchYahooQuotesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load Yahoo stock radar quote data.");
            }

            var loadedSymbols = ideas
                .Select(i => i.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(apiKey) && ideas.Count < 10)
            {
                foreach (var symbol in Symbols.Where(symbol => !loadedSymbols.Contains(symbol)))
                {
                    try
                    {
                        var idea = await FetchCompanyOverviewAsync(symbol, apiKey, cancellationToken);
                        if (idea is not null)
                        {
                            ideas.Add(idea);
                            loadedSymbols.Add(symbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load Alpha Vantage stock data for {Symbol}.", symbol);
                    }

                    if (ideas.Count >= 10)
                        break;
                }
            }

            if (ideas.Count < 10)
            {
                foreach (var symbol in Symbols.Where(symbol => !loadedSymbols.Contains(symbol)))
                {
                    try
                    {
                        var idea = await FetchYahooQuoteSummaryAsync(symbol, cancellationToken);
                        if (idea is not null)
                        {
                            ideas.Add(idea);
                            loadedSymbols.Add(symbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load Yahoo stock summary for {Symbol}.", symbol);
                    }

                    if (ideas.Count >= 10)
                        break;
                }
            }

            var ordered = ideas
                .Where(i => i.Price.HasValue && (i.TargetUpsidePercent.HasValue || i.PeRatio.HasValue || i.PegRatio.HasValue))
                .GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(i => i.Score).First())
                .OrderByDescending(i => i.Score)
                .ThenByDescending(i => i.TargetUpsidePercent ?? 0)
                .Take(10)
                .ToList();

            if (ordered.Count < 10)
            {
                var alreadyShown = ordered
                    .Select(i => i.Symbol)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                ordered.AddRange(FallbackIdeas
                    .Where(i => !alreadyShown.Contains(i.Key))
                    .Select(i => BuildFallbackIdea(i.Key, i.Value))
                    .OrderByDescending(i => i.Score)
                    .Take(10 - ordered.Count));
            }

            await EnrichAthDiscountsAsync(ordered, cancellationToken);

            return new StockRadarResult
            {
                IsConfigured = !string.IsNullOrWhiteSpace(apiKey),
                UpdatedAt = DateTimeOffset.UtcNow,
                Ideas = ordered,
                Message = ideas.Count == 0
                    ? "Live data ted nejsou dostupna, zobrazuji orientacni watchlist."
                    : "Akcie k provereni podle fundamentu."
            };
        }

        private async Task<List<StockIdeaCard>> FetchYahooQuotesAsync(CancellationToken cancellationToken)
        {
            var client = CreateClient();
            var symbols = string.Join(",", Symbols.Select(Uri.EscapeDataString));
            var url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbols}";
            var json = await client.GetStringAsync(url, cancellationToken);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("quoteResponse", out var response) ||
                !response.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ideas = new List<StockIdeaCard>();

            foreach (var item in results.EnumerateArray())
            {
                var symbol = ReadYahooString(item, "symbol");
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                var idea = BuildIdea(
                    symbol,
                    ReadYahooString(item, "longName") ?? ReadYahooString(item, "shortName") ?? StockNames.GetValueOrDefault(symbol, symbol),
                    ReadYahooString(item, "sector") ?? "Market",
                    ReadYahooDecimal(item, "regularMarketPrice") ?? ReadYahooDecimal(item, "fiftyDayAverage"),
                    ReadYahooDecimal(item, "targetMeanPrice"),
                    ReadYahooDecimal(item, "fiftyTwoWeekHigh"),
                    ReadYahooDecimal(item, "trailingPE") ?? ReadYahooDecimal(item, "forwardPE"),
                    ReadYahooDecimal(item, "trailingPegRatio"),
                    null,
                    ReadYahooDecimal(item, "revenueGrowth"),
                    "Yahoo");

                if (idea is not null)
                    ideas.Add(idea);
            }

            return ideas;
        }

        private async Task<StockIdeaCard?> FetchYahooQuoteSummaryAsync(string symbol, CancellationToken cancellationToken)
        {
            var client = CreateClient();
            var url = $"https://query1.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}?modules=price,summaryDetail,financialData,defaultKeyStatistics,summaryProfile";
            var json = await client.GetStringAsync(url, cancellationToken);

            using var document = JsonDocument.Parse(json);
            var result = document.RootElement
                .GetProperty("quoteSummary")
                .GetProperty("result")[0];

            var priceModule = result.GetProperty("price");
            var financialData = result.TryGetProperty("financialData", out var financial) ? financial : default;
            var keyStats = result.TryGetProperty("defaultKeyStatistics", out var stats) ? stats : default;
            var profile = result.TryGetProperty("summaryProfile", out var summaryProfile) ? summaryProfile : default;
            var summaryDetail = result.TryGetProperty("summaryDetail", out var summary) ? summary : default;

            return BuildIdea(
                symbol,
                ReadYahooString(priceModule, "longName") ?? ReadYahooString(priceModule, "shortName") ?? StockNames.GetValueOrDefault(symbol, symbol),
                ReadYahooString(profile, "sector") ?? "Market",
                ReadYahooRaw(priceModule, "regularMarketPrice") ?? ReadYahooRaw(financialData, "currentPrice"),
                ReadYahooRaw(financialData, "targetMeanPrice"),
                ReadYahooRaw(summaryDetail, "fiftyTwoWeekHigh"),
                ReadYahooRaw(priceModule, "trailingPE") ?? ReadYahooRaw(keyStats, "forwardPE"),
                ReadYahooRaw(keyStats, "pegRatio"),
                null,
                ReadYahooRaw(financialData, "revenueGrowth") is { } growth ? growth * 100m : null,
                "Yahoo");
        }

        private async Task<StockIdeaCard?> FetchCompanyOverviewAsync(string symbol, string apiKey, CancellationToken cancellationToken)
        {
            var client = CreateClient();
            var url = $"https://www.alphavantage.co/query?function=OVERVIEW&symbol={Uri.EscapeDataString(symbol)}&apikey={Uri.EscapeDataString(apiKey)}";
            var json = await client.GetStringAsync(url, cancellationToken);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("Note", out _) ||
                root.TryGetProperty("Information", out _) ||
                !root.TryGetProperty("Symbol", out var symbolElement) ||
                string.IsNullOrWhiteSpace(symbolElement.GetString()))
            {
                return null;
            }

            var revenueGrowth = ReadDecimal(root, "QuarterlyRevenueGrowthYOY");
            return BuildIdea(
                symbol,
                ReadString(root, "Name") ?? StockNames.GetValueOrDefault(symbol, symbol),
                ReadString(root, "Sector") ?? "Market",
                ReadDecimal(root, "50DayMovingAverage"),
                ReadDecimal(root, "AnalystTargetPrice"),
                ReadDecimal(root, "52WeekHigh"),
                ReadDecimal(root, "PERatio"),
                ReadDecimal(root, "PEGRatio"),
                ReadDecimal(root, "ProfitMargin"),
                revenueGrowth.HasValue ? revenueGrowth.Value * 100m : null,
                "Alpha Vantage");
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeDesk/1.0");
            return client;
        }

        private static StockIdeaCard? BuildIdea(
            string symbol,
            string name,
            string sector,
            decimal? price,
            decimal? target,
            decimal? athPrice,
            decimal? pe,
            decimal? peg,
            decimal? margin,
            decimal? revenueGrowthPercent,
            string source)
        {
            if (!price.HasValue && !target.HasValue && !pe.HasValue && !peg.HasValue)
                return null;

            decimal? upside = price.HasValue && price.Value > 0 && target.HasValue
                ? (target.Value - price.Value) / price.Value * 100m
                : null;

            decimal? discountFromAth = price.HasValue && price.Value > 0 && athPrice.HasValue && athPrice.Value > price.Value
                ? (athPrice.Value - price.Value) / athPrice.Value * 100m
                : null;

            var score = 0;
            var reasons = new List<string>();

            if (upside >= 20)
            {
                score += 3;
                reasons.Add($"Analyst target je cca +{upside.Value:0.#}% nad aktualni cenou.");
            }
            else if (upside >= 10)
            {
                score += 2;
                reasons.Add($"Target ukazuje cca +{upside.Value:0.#}% prostor.");
            }

            if (peg is > 0 and <= 1.8m)
            {
                score += 2;
                reasons.Add($"PEG {peg:0.##} vypada rozumne vuci rustu.");
            }
            else if (pe is > 0 and <= 28m)
            {
                score += 1;
                reasons.Add($"P/E {pe:0.#} neni extremne vysoke.");
            }

            if (margin > 0.12m)
            {
                score += 1;
                reasons.Add($"Firma ma kladnou marzi {margin.Value * 100m:0.#}%.");
            }

            if (revenueGrowthPercent > 8m)
            {
                score += 1;
                reasons.Add($"Trzby rostou mezirocne o {revenueGrowthPercent.Value:0.#}%.");
            }

            if (reasons.Count == 0)
            {
                if (target.HasValue && price.HasValue)
                    reasons.Add("Akcie ma vyplneny analyticky target i aktualni cenu.");
                else if (pe.HasValue)
                    reasons.Add($"K dispozici je valuacni metrika P/E {pe:0.#}.");
                else
                    reasons.Add("Zakladni trzni data jsou k dispozici.");
            }

            return new StockIdeaCard
            {
                Symbol = symbol,
                Name = name,
                Sector = sector,
                Score = score,
                Price = price,
                TargetPrice = target,
                TargetUpsidePercent = upside,
                AllTimeHighPrice = athPrice,
                DiscountFromAthPercent = discountFromAth,
                PeRatio = pe,
                PegRatio = peg,
                RevenueGrowthPercent = revenueGrowthPercent,
                Reasons = reasons.Take(2).ToList(),
                Source = source
            };
        }

        private static StockIdeaCard BuildFallbackIdea(string symbol, StockFallback fallback)
        {
            var reasons = new[]
            {
                fallback.Reason,
                fallback.RevenueGrowthPercent > 10
                    ? $"Orientacni rust trzeb: {fallback.RevenueGrowthPercent:0.#}%."
                    : $"Orientacni P/E: {fallback.PeRatio:0.#}."
            };

            decimal? discountFromAth = fallback.AthPrice > fallback.Price
                ? (fallback.AthPrice - fallback.Price) / fallback.AthPrice * 100m
                : null;

            return new StockIdeaCard
            {
                Symbol = symbol,
                Name = fallback.Name,
                Sector = fallback.Sector,
                Score = fallback.Score,
                Price = fallback.Price,
                AllTimeHighPrice = fallback.AthPrice,
                DiscountFromAthPercent = discountFromAth,
                PeRatio = fallback.PeRatio,
                PegRatio = fallback.PegRatio,
                RevenueGrowthPercent = fallback.RevenueGrowthPercent,
                Reasons = reasons,
                Source = "Fallback"
            };
        }

        private async Task EnrichAthDiscountsAsync(IReadOnlyList<StockIdeaCard> ideas, CancellationToken cancellationToken)
        {
            var tasks = ideas.Select(async idea =>
            {
                try
                {
                    var snapshot = await FetchYahooAthSnapshotAsync(idea.Symbol, cancellationToken);
                    if (snapshot is null)
                        return;

                    idea.Price = snapshot.LastClose;
                    idea.AllTimeHighPrice = snapshot.HighestHigh;
                    idea.DiscountFromAthPercent = snapshot.HighestHigh > snapshot.LastClose
                        ? (snapshot.HighestHigh - snapshot.LastClose) / snapshot.HighestHigh * 100m
                        : 0m;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load Yahoo ATH data for {Symbol}.", idea.Symbol);
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task<StockAthSnapshot?> FetchYahooAthSnapshotAsync(string symbol, CancellationToken cancellationToken)
        {
            var client = CreateClient();
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=max&interval=1wk";
            var json = await client.GetStringAsync(url, cancellationToken);

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("chart", out var chart) ||
                !chart.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var result = results[0];

            var currentPrice = result.TryGetProperty("meta", out var meta)
                ? ReadYahooDecimal(meta, "regularMarketPrice")
                : null;

            if (!result.TryGetProperty("indicators", out var indicators) ||
                !indicators.TryGetProperty("quote", out var quotes) ||
                quotes.ValueKind != JsonValueKind.Array ||
                quotes.GetArrayLength() == 0)
            {
                return null;
            }

            var quote = quotes[0];

            decimal? highestHigh = null;
            if (quote.TryGetProperty("high", out var highs) && highs.ValueKind == JsonValueKind.Array)
            {
                foreach (var highElement in highs.EnumerateArray())
                {
                    if (highElement.ValueKind != JsonValueKind.Number)
                        continue;

                    var high = Convert.ToDecimal(highElement.GetDouble(), CultureInfo.InvariantCulture);
                    highestHigh = highestHigh.HasValue ? Math.Max(highestHigh.Value, high) : high;
                }
            }

            decimal? lastClose = null;
            if (quote.TryGetProperty("close", out var closes) && closes.ValueKind == JsonValueKind.Array)
            {
                foreach (var closeElement in closes.EnumerateArray())
                {
                    if (closeElement.ValueKind == JsonValueKind.Number)
                    {
                        lastClose = Convert.ToDecimal(closeElement.GetDouble(), CultureInfo.InvariantCulture);
                    }
                }
            }

            var price = currentPrice ?? lastClose;

            return highestHigh.HasValue && price.HasValue
                ? new StockAthSnapshot(price.Value, highestHigh.Value)
                : null;
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String &&
                   !string.Equals(value.GetString(), "None", StringComparison.OrdinalIgnoreCase)
                ? value.GetString()
                : null;
        }

        private static decimal? ReadDecimal(JsonElement root, string propertyName)
        {
            var value = ReadString(root, propertyName);
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static string? ReadYahooString(JsonElement root, string propertyName)
        {
            return root.ValueKind != JsonValueKind.Undefined &&
                   root.ValueKind != JsonValueKind.Null &&
                   root.TryGetProperty(propertyName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static decimal? ReadYahooDecimal(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Undefined ||
                root.ValueKind == JsonValueKind.Null ||
                !root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number => Convert.ToDecimal(value.GetDouble(), CultureInfo.InvariantCulture),
                JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null
            };
        }

        private static decimal? ReadYahooRaw(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Undefined ||
                root.ValueKind == JsonValueKind.Null ||
                !root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("raw", out var raw) &&
                raw.ValueKind == JsonValueKind.Number)
            {
                return Convert.ToDecimal(raw.GetDouble(), CultureInfo.InvariantCulture);
            }

            return ReadYahooDecimal(root, propertyName);
        }
    }

    public class StockRadarResult
    {
        public IReadOnlyList<StockIdeaCard> Ideas { get; set; } = [];
        public DateTimeOffset? UpdatedAt { get; set; }
        public bool IsConfigured { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    internal sealed record StockFallback(
        string Name,
        string Sector,
        int Score,
        decimal PeRatio,
        decimal PegRatio,
        decimal RevenueGrowthPercent,
        decimal Price,
        decimal AthPrice,
        string Reason);

    internal sealed record StockAthSnapshot(decimal LastClose, decimal HighestHigh);
}
