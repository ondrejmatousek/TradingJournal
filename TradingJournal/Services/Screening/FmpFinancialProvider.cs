using System.Globalization;
using System.Text.Json;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Services.Screening;

/// <summary>Optional secondary source. Missing facts remain missing, including on partial API failures.</summary>
public sealed class FmpFinancialProvider(IHttpClientFactory factory, IConfiguration configuration)
{
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> RetryAfter = new();
    public async Task<(FundamentalInput? Fundamentals, FinancialDataset? Financials)> FetchAsync(string symbol, DateOnly today, CancellationToken ct)
    {
        var result = await FetchDetailedAsync(symbol, today, ct);
        return (result.Fundamentals, result.Financials);
    }

    public async Task<FinancialFetchResult> FetchDetailedAsync(string symbol, DateOnly today, CancellationToken ct)
    {
        var key = configuration["FinancialModelingPrep:ApiKey"] ?? configuration["FinancialModelingPrep__ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) return new(null, null, [new("configuration", "FMP", "Chybí FinancialModelingPrep:ApiKey pro záložní výkazy.")]);
        await RequestGate.WaitAsync(ct);
        try { return await FetchCoreAsync(symbol, today, key, ct); }
        finally { RequestGate.Release(); }
    }

    private async Task<FinancialFetchResult> FetchCoreAsync(string symbol, DateOnly today, string key, CancellationToken ct)
    {
        var keyId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        var issues = new List<DataIssue>();
        async Task<JsonElement> Get(string endpoint)
        {
            if (RetryAfter.TryGetValue(keyId, out var until) && until > DateTimeOffset.UtcNow)
            {
                if (!issues.Any(i => i.Code == "rate-limit")) issues.Add(new("rate-limit", "FMP", $"FMP: HTTP 429; další požadavky pozastaveny do {until:O}."));
                return default;
            }
            try
            {
                using var response = await client.GetAsync($"https://financialmodelingprep.com/stable/{endpoint}?symbol={Uri.EscapeDataString(symbol)}&period=annual&limit=6&apikey={Uri.EscapeDataString(key)}", ct);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var delay = response.Headers.RetryAfter;
                    var retryAt = delay?.Date ?? DateTimeOffset.UtcNow.Add(delay?.Delta ?? TimeSpan.FromMinutes(15));
                    RetryAfter[keyId] = retryAt > DateTimeOffset.UtcNow ? retryAt : DateTimeOffset.UtcNow.AddMinutes(15);
                    issues.Add(new("rate-limit", "FMP", $"FMP: HTTP 429; další požadavky pozastaveny do {RetryAfter[keyId]:O}."));
                    return default;
                }
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (json.RootElement.ValueKind != JsonValueKind.Array)
                {
                    issues.Add(new("api", "FMP", $"FMP {endpoint}: místo výkazů přišla chybová odpověď."));
                    return default;
                }
                return json.RootElement.Clone();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException)
            { issues.Add(DataIssue.FromException($"FMP {endpoint}", ex)); return default; }
        }
        var income = await Get("income-statement");
        var balance = await Get("balance-sheet-statement");
        var cash = await Get("cash-flow-statement");
        var result = Parse(symbol, today, income, balance, cash);
        return result with { Issues = issues.Concat(result.Issues).ToList() };
    }

    public static FinancialFetchResult Parse(string symbol, DateOnly today, JsonElement income, JsonElement balance, JsonElement cashflow)
    {
        var issues = new List<DataIssue>();
        var periods = new Dictionary<DateOnly, FinancialPeriod>();
        string? currency = null;
        void Read(JsonElement root, string endpoint, (string Tag, string Key, bool Absolute, bool Instant)[] mappings)
        {
            if (root.ValueKind != JsonValueKind.Array) return;
            foreach (var row in root.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                { issues.Add(new("invalid", "FMP", $"{endpoint}: neplatný řádek výkazu.")); continue; }
                if (Text(row, "symbol") != symbol || Text(row, "period") != "FY" || !Date(row, "date", out var end) || end > today) continue;
                if (!Date(row, "filingDate", out var filed) && !Date(row, "fillingDate", out filed))
                { issues.Add(new("missing", "FMP", $"{endpoint} {end}: chybí datum podání.")); continue; }
                if (filed < end || filed > today) continue;
                var unit = Text(row, "reportedCurrency");
                if (string.IsNullOrWhiteSpace(unit)) { issues.Add(new("currency", "FMP", $"{end}: chybí měna výkazu.")); continue; }
                if (currency != null && currency != unit) { issues.Add(new("currency", "FMP", "Historie obsahuje různé měny; nesrovnatelné období vynecháno.")); continue; }
                currency ??= unit;
                // Annual endpoint supplies the fiscal year end, not a calendar-year start.
                // Preserve this derivation so it cannot silently match an explicit SEC period.
                var start = end.AddYears(-1).AddDays(1);
                if (!periods.TryGetValue(end, out var period)) periods[end] = period = new(start, end, false, []);
                foreach (var (tag, name, absolute, instant) in mappings)
                {
                    if (!row.TryGetProperty(tag, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount)) continue;
                    if (period.Facts.TryGetValue(name, out var existing) && existing.Filed >= filed) continue;
                    period.Facts[name] = new(absolute ? Math.Abs(amount) : amount, name.EndsWith("Shares") ? "shares" : unit,
                        instant ? end : start, end, filed, $"https://financialmodelingprep.com/stable/{endpoint}?symbol={Uri.EscapeDataString(symbol)}",
                        tag, $"fmp-{symbol}-{end:yyyyMMdd}-{filed:yyyyMMdd}", Derivation: instant ? "" : "FY start derived from annual fiscal end.");
                }
            }
        }
        Read(income, "income-statement", [("revenue","Revenue",false,false), ("operatingIncome","Ebit",false,false),
            ("netIncome","NetIncome",false,false), ("incomeBeforeTax","Pretax",false,false), ("incomeTaxExpense","Tax",false,false),
            ("weightedAverageShsOutDil","DilutedShares",false,false), ("weightedAverageShsOut","BasicShares",false,false)]);
        Read(balance, "balance-sheet-statement", [("cashAndCashEquivalents","Cash",false,true), ("totalDebt","TotalDebt",false,true),
            ("totalAssets","Assets",false,true), ("totalLiabilities","Liabilities",false,true), ("netReceivables","Receivables",false,true),
            ("inventory","Inventory",false,true), ("accountPayables","Payables",false,true)]);
        Read(cashflow, "cash-flow-statement", [("operatingCashFlow","Ocf",false,false), ("netCashProvidedByOperatingActivities","Ocf",false,false),
            ("capitalExpenditure","CapEx",true,false), ("depreciationAndAmortization","Da",false,false)]);
        var history = periods.Values.OrderBy(x => x.End).ToList();
        if (history.Count == 0) return new(null, null, issues.Append(new DataIssue("missing", "FMP", "Žádné použitelné roční výkazy.")).ToList());
        if (currency != "USD") issues.Add(new("currency", "FMP", $"Měna {currency} není podporovaná oceňovacím modelem; nebyla zaměněna za USD."));
        var dataset = new FinancialDataset(currency!, today, history, history[^1], null, ["Secondary source: FMP; annual fiscal start derived from end date; weighted-average shares, not actual shares."]);
        return new(FinancialDataMerge.FromDataset(dataset), dataset, issues);
    }

    private static string? Text(JsonElement row, string key) => row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Date(JsonElement row, string key, out DateOnly date) => DateOnly.TryParse(Text(row, key), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
