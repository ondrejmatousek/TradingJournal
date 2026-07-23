using System.Globalization;
using System.Text.Json;

namespace TradingJournal.Services
{
    public class UnderEma200HistoryProvider : IUnderEma200HistoryProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UnderEma200HistoryProvider> _logger;

        public UnderEma200HistoryProvider(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<UnderEma200HistoryProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<UnderEma200PricePoint>> GetDailyPricesAsync(string symbol, CancellationToken cancellationToken)
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeDesk/1.0");

            var yahooPrices = await TryFetchDailyPricesAsync(
                symbol,
                "Yahoo chart",
                () => FetchYahooDailyPricesAsync(client, symbol, cancellationToken));
            if (yahooPrices.Count > 0)
            {
                return yahooPrices;
            }

            var prices = await TryFetchDailyPricesAsync(
                symbol,
                "Alpha Vantage",
                () => FetchAlphaVantageDailyPricesAsync(client, symbol, cancellationToken));
            if (prices.Count > 0)
            {
                return prices;
            }

            return await TryFetchDailyPricesAsync(
                symbol,
                "Stooq",
                () => FetchStooqDailyPricesAsync(client, symbol, cancellationToken));
        }

        private async Task<List<UnderEma200PricePoint>> TryFetchDailyPricesAsync(
            string symbol,
            string providerName,
            Func<Task<List<UnderEma200PricePoint>>> fetch)
        {
            try
            {
                var prices = await fetch();
                if (prices.Count == 0)
                {
                    _logger.LogWarning(
                        "{ProviderName} returned zero daily prices for EMA200 symbol {Symbol}.",
                        providerName,
                        symbol);
                }

                return prices;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{ProviderName} failed while loading EMA200 daily prices for symbol {Symbol}.",
                    providerName,
                    symbol);
                return [];
            }
        }

        private async Task<List<UnderEma200PricePoint>> FetchYahooDailyPricesAsync(HttpClient client, string symbol, CancellationToken cancellationToken)
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=10y&interval=1d&events=history";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo chart returned {StatusCode} for EMA200 symbol {Symbol}.", response.StatusCode, symbol);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("chart", out var chart) ||
                !chart.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                _logger.LogWarning("Yahoo chart response for EMA200 symbol {Symbol} did not contain chart.result.", symbol);
                return [];
            }

            var result = results[0];
            if (!result.TryGetProperty("timestamp", out var timestamps) ||
                timestamps.ValueKind != JsonValueKind.Array ||
                !result.TryGetProperty("indicators", out var indicators) ||
                !indicators.TryGetProperty("quote", out var quotes) ||
                quotes.ValueKind != JsonValueKind.Array ||
                quotes.GetArrayLength() == 0 ||
                !quotes[0].TryGetProperty("close", out var closes) ||
                closes.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Yahoo chart response for EMA200 symbol {Symbol} did not contain daily close arrays.", symbol);
                return [];
            }

            var prices = new List<UnderEma200PricePoint>();
            var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                if (timestamps[i].ValueKind != JsonValueKind.Number ||
                    closes[i].ValueKind != JsonValueKind.Number ||
                    !timestamps[i].TryGetInt64(out var unixTime) ||
                    !closes[i].TryGetDecimal(out var close) ||
                    close <= 0)
                {
                    continue;
                }

                var date = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime.Date;
                prices.Add(new UnderEma200PricePoint(date, close));
            }

            return prices
                .GroupBy(price => price.Date)
                .Select(group => group.Last())
                .OrderBy(price => price.Date)
                .ToList();
        }

        private async Task<List<UnderEma200PricePoint>> FetchAlphaVantageDailyPricesAsync(HttpClient client, string symbol, CancellationToken cancellationToken)
        {
            var apiKey = _configuration["AlphaVantage:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = _configuration["AlphaVantage__ApiKey"];
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return [];
            }

            var url = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY_ADJUSTED&symbol={Uri.EscapeDataString(symbol)}&outputsize=full&apikey={Uri.EscapeDataString(apiKey)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Alpha Vantage returned {StatusCode} for EMA200 symbol {Symbol}.", response.StatusCode, symbol);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("Time Series (Daily)", out var timeSeries))
            {
                _logger.LogWarning("Alpha Vantage response for EMA200 symbol {Symbol} did not contain Time Series (Daily).", symbol);
                return [];
            }

            var prices = new List<UnderEma200PricePoint>();
            foreach (var day in timeSeries.EnumerateObject())
            {
                if (!DateTime.TryParseExact(day.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (!TryReadAlphaDecimal(day.Value, "5. adjusted close", out var close) &&
                    !TryReadAlphaDecimal(day.Value, "4. close", out close))
                {
                    continue;
                }

                prices.Add(new UnderEma200PricePoint(date, close));
            }

            return prices.OrderBy(price => price.Date).ToList();
        }

        private async Task<List<UnderEma200PricePoint>> FetchStooqDailyPricesAsync(HttpClient client, string symbol, CancellationToken cancellationToken)
        {
            var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={symbol.ToLowerInvariant()}.us&i=d", cancellationToken);
            if (string.IsNullOrWhiteSpace(csv) ||
                !csv.TrimStart().StartsWith("Date,", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Stooq response for EMA200 symbol {Symbol} did not contain CSV daily prices.", symbol);
                return [];
            }

            var prices = new List<UnderEma200PricePoint>();
            foreach (var line in csv.Split('\n').Skip(1))
            {
                var parts = line.Trim().Split(',');
                if (parts.Length < 5 ||
                    !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    !decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                {
                    continue;
                }

                prices.Add(new UnderEma200PricePoint(date, close));
            }

            return prices.OrderBy(price => price.Date).ToList();
        }

        private static bool TryReadAlphaDecimal(JsonElement source, string propertyName, out decimal value)
        {
            value = 0;
            return source.TryGetProperty(propertyName, out var property) &&
                decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }
    }
}
