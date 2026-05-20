using System.Globalization;
using System.Text.Json;
using TradingJournal.ViewModels;

namespace TradingJournal.Services
{
    public class StockRadarService
    {
        private static readonly string[] Symbols =
        [
            "MSFT", "GOOGL", "AMZN", "AAPL", "META", "NVDA", "AMD", "ASML", "TSM", "AVGO"
        ];

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
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new StockRadarResult
                {
                    IsConfigured = false,
                    Message = "Dopln AlphaVantage:ApiKey pro live data."
                };
            }

            var ideas = new List<StockIdeaCard>();

            foreach (var symbol in Symbols)
            {
                try
                {
                    var idea = await FetchCompanyOverviewAsync(symbol, apiKey, cancellationToken);
                    if (idea is not null)
                        ideas.Add(idea);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load stock radar data for {Symbol}.", symbol);
                }
            }

            var ordered = ideas
                .Where(i => i.Score > 0)
                .OrderByDescending(i => i.Score)
                .ThenByDescending(i => i.TargetUpsidePercent ?? 0)
                .Take(3)
                .ToList();

            return new StockRadarResult
            {
                IsConfigured = true,
                UpdatedAt = DateTimeOffset.UtcNow,
                Ideas = ordered,
                Message = ordered.Count == 0
                    ? "Ted nemam zadny silny kandidat podle fundamentu."
                    : "Akcie k prověření podle fundamentu."
            };
        }

        private async Task<StockIdeaCard?> FetchCompanyOverviewAsync(string symbol, string apiKey, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeDesk/1.0");

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

            var price = ReadDecimal(root, "50DayMovingAverage");
            var target = ReadDecimal(root, "AnalystTargetPrice");
            var pe = ReadDecimal(root, "PERatio");
            var peg = ReadDecimal(root, "PEGRatio");
            var margin = ReadDecimal(root, "ProfitMargin");
            var revenueGrowth = ReadDecimal(root, "QuarterlyRevenueGrowthYOY");
            decimal? upside = price.HasValue && price.Value > 0 && target.HasValue
                ? (target.Value - price.Value) / price.Value * 100m
                : null;

            var score = 0;
            var reasons = new List<string>();

            if (upside >= 20)
            {
                score += 3;
                reasons.Add($"Analyst target je cca +{upside.Value:0.#}% nad 50D cenou.");
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

            if (revenueGrowth > 0.08m)
            {
                score += 1;
                reasons.Add($"Trzby rostou mezirocne o {revenueGrowth.Value * 100m:0.#}%.");
            }

            return new StockIdeaCard
            {
                Symbol = symbol,
                Name = ReadString(root, "Name") ?? symbol,
                Sector = ReadString(root, "Sector") ?? "Market",
                Score = score,
                Price = price,
                TargetPrice = target,
                TargetUpsidePercent = upside,
                PeRatio = pe,
                PegRatio = peg,
                RevenueGrowthPercent = revenueGrowth.HasValue ? revenueGrowth.Value * 100m : null,
                Reasons = reasons.Take(2).ToList(),
                Source = "Alpha Vantage"
            };
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
    }

    public class StockRadarResult
    {
        public IReadOnlyList<StockIdeaCard> Ideas { get; set; } = [];
        public DateTimeOffset? UpdatedAt { get; set; }
        public bool IsConfigured { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
