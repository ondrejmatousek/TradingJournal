using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace TradingJournal.Services.Screening;

public sealed class ScreeningDataProvider(IHttpClientFactory factory, IConfiguration configuration, ScreeningStore store, IWebHostEnvironment? environment = null)
{
    private static readonly SemaphoreSlim SecGate = new(1, 1);
    private static DateTimeOffset secRetryAfter;
    public async Task<FundamentalInput?> FundamentalsAsync(string symbol, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", configuration["Screening:SecUserAgent"] ?? "EdgeDesk/1.0 investment-research");
        await SecGate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow < secRetryAfter)
                throw new InvalidOperationException("SEC temporarily unavailable; retry after cooldown.");
            var mapping = await store.ReadAsync<Dictionary<string, long>>("sec-tickers-v1", ct);
            if (mapping == null || !mapping.ContainsKey(symbol))
            {
                var fallback = await store.ReadAsync<Dictionary<string, long>>("sec-tickers-fallback-v1", ct);
                if (fallback == null)
                {
                    try
                    {
                        using var tickers = JsonDocument.Parse(await client.GetStringAsync("https://www.sec.gov/files/company_tickers.json", ct));
                        mapping = tickers.RootElement.EnumerateObject().Select(p => p.Value).ToDictionary(p => p.GetProperty("ticker").GetString()!, p => p.GetProperty("cik_str").GetInt64());
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
                    {
                        var path = Path.Combine(environment?.ContentRootPath ?? AppContext.BaseDirectory, "Docs", "investments", "sec-tickers.json");
                        fallback = JsonSerializer.Deserialize<Dictionary<string, long>>(await File.ReadAllTextAsync(path, ct)) ?? [];
                        await store.WriteAsync("sec-tickers-fallback-v1", fallback, ct);
                    }
                }
                if (fallback != null)
                {
                    if (!fallback.TryGetValue(symbol, out var candidateCik)) return null;
                    // Bundled identifiers are only hints. Confirm the ticker with SEC before using financial facts.
                    using var submission = JsonDocument.Parse(await client.GetStringAsync($"https://data.sec.gov/submissions/CIK{candidateCik:0000000000}.json", ct));
                    if (!submission.RootElement.TryGetProperty("tickers", out var listed) || !listed.EnumerateArray().Any(t => t.GetString() == symbol))
                        return null;
                    mapping ??= [];
                    mapping[symbol] = candidateCik;
                }
                await store.WriteAsync("sec-tickers-v1", mapping, ct);
            }
            if (mapping == null || !mapping.TryGetValue(symbol, out var cik)) return null;
            var url = $"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik:0000000000}.json";
            using var facts = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            return ParseFundamentals(facts.RootElement, url, DateOnly.FromDateTime(DateTime.UtcNow));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests || (int?)ex.StatusCode >= 500)
        {
            secRetryAfter = DateTimeOffset.UtcNow.AddMinutes(15);
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            secRetryAfter = DateTimeOffset.UtcNow.AddMinutes(15);
            throw;
        }
        finally { SecGate.Release(); }
    }

    public static FundamentalInput? ParseFundamentals(JsonElement root, string source, DateOnly today)
    {
        if (!root.TryGetProperty("facts", out var facts) || !facts.TryGetProperty("us-gaap", out var gaap)) return null;
        List<(DateOnly Start, DateOnly End, DateOnly Filed, decimal Value)> Read(JsonElement taxonomy, string unit, params string[] tags)
        {
            var rows = new List<(DateOnly, DateOnly, DateOnly, decimal)>();
            foreach (var tag in tags)
            {
                if (!taxonomy.TryGetProperty(tag, out var field) || !field.TryGetProperty("units", out var units) || !units.TryGetProperty(unit, out var values)) continue;
                foreach (var item in values.EnumerateArray())
                {
                    if (!item.TryGetProperty("end", out var end) || !item.TryGetProperty("filed", out var filed) || !item.TryGetProperty("val", out var val) || !val.TryGetDecimal(out var amount)) continue;
                    var endDate = DateOnly.Parse(end.GetString()!, CultureInfo.InvariantCulture);
                    var filedDate = DateOnly.Parse(filed.GetString()!, CultureInfo.InvariantCulture);
                    if (endDate > today || filedDate > today) continue;
                    var start = item.TryGetProperty("start", out var st) ? DateOnly.Parse(st.GetString()!, CultureInfo.InvariantCulture) : endDate;
                    rows.Add((start, endDate, filedDate, amount));
                }
            }
            return rows;
        }
        var revenue = Read(gaap, "USD", "RevenueFromContractWithCustomerExcludingAssessedTax", "Revenues", "SalesRevenueNet")
            .Where(r => r.End.DayNumber - r.Start.DayNumber is >= 330 and <= 380).OrderByDescending(r => r.End).ThenByDescending(r => r.Filed).ToArray();
        if (revenue.Length == 0) return null;
        var current = revenue[0];
        var previous = revenue.FirstOrDefault(r => current.End.DayNumber - r.End.DayNumber is >= 330 and <= 400);
        decimal? Annual(params string[] tags) => Read(gaap, "USD", tags).Where(r => r.Start == current.Start && r.End == current.End).OrderByDescending(r => r.Filed).Select(r => (decimal?)r.Value).FirstOrDefault();
        decimal? Instant(params string[] tags) => Read(gaap, "USD", tags).Where(r => r.Start == r.End && r.End == current.End).OrderByDescending(r => r.Filed).Select(r => (decimal?)r.Value).FirstOrDefault();
        var cash = Annual("NetCashProvidedByUsedInOperatingActivities");
        var capex = Annual("PaymentsToAcquirePropertyPlantAndEquipment");
        var income = Annual("NetIncomeLoss");
        var assets = Instant("Assets"); var liabilities = Instant("Liabilities");
        // Use the latest reported actual share count, not weighted-average diluted shares from a different split basis.
        var shareRows = facts.TryGetProperty("dei", out var dei) ? Read(dei, "shares", "EntityCommonStockSharesOutstanding") : [];
        var shares = shareRows.Where(r => r.Start == r.End && today.DayNumber - r.End.DayNumber <= 180).OrderByDescending(r => r.End).ThenByDescending(r => r.Filed).FirstOrDefault();
        if (cash == null || capex == null || income == null || assets == null || liabilities == null || shares.Value <= 0 || previous.Value <= 0) return null;
        return new(current.End, current.Filed, current.Value, previous.Value, cash.Value, capex.Value, income.Value, assets.Value, liabilities.Value, shares.Value, source);
    }

    public async Task<List<MacroObservation>> MacroAsync(CancellationToken ct)
    {
        using var client = factory.CreateClient(); client.Timeout = TimeSpan.FromSeconds(15);
        var observations = new List<MacroObservation>();
        foreach (var series in new[] { "DGS10", "UNRATE" })
        {
            string csv;
            try { csv = await ProviderHttp.GetStringAsync(client, $"https://fred.stlouisfed.org/graph/fredgraph.csv?id={series}&cosd={DateTime.UtcNow.AddYears(-1):yyyy-MM-dd}", ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var url = series == "UNRATE" ? "https://api.bls.gov/publicAPI/v2/timeseries/data/LNS14000000"
                    : $"https://home.treasury.gov/resource-center/data-chart-center/interest-rates/pages/xml?data=daily_treasury_yield_curve&field_tdr_date_value={today.Year}";
                var body = await ProviderHttp.GetStringAsync(client, url, ct);
                if (series == "DGS10" && today.Month <= 4)
                {
                    var priorBody = await ProviderHttp.GetStringAsync(client, url.Replace($"={today.Year}", $"={today.Year - 1}"), ct);
                    var xml = XDocument.Parse(body);
                    xml.Root!.Add(XDocument.Parse(priorBody).Root!.Elements().Where(e => e.Name.LocalName == "entry"));
                    body = xml.ToString();
                }
                observations.Add(ParseOfficialMacro(series, body, today));
                continue;
            }
            var parsed = csv.Split('\n').Skip(1).Select(line => line.Trim().Split(','))
                .Where(c => c.Length == 2 && DateOnly.TryParse(c[0], out _) && decimal.TryParse(c[1], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .Select(c => (Date: DateOnly.Parse(c[0], CultureInfo.InvariantCulture), Value: decimal.Parse(c[1], CultureInfo.InvariantCulture)))
                .Where(p => p.Date <= DateOnly.FromDateTime(DateTime.UtcNow)).OrderBy(p => p.Date).ToArray();
            if (parsed.Length == 0) continue;
            var last = parsed[^1]; var prior = parsed.Where(p => p.Date <= last.Date.AddMonths(-3)).ToArray();
            observations.Add(new(series, last.Date, last.Value, prior.Length > 0 ? prior[^1].Value : null, $"https://fred.stlouisfed.org/series/{series}"));
        }
        if (observations.Count != 2) throw new InvalidDataException("Incomplete macro data; preserving previous cache.");
        await store.WriteAsync("macro-v1", observations, ct);
        return observations;
    }

    public async Task<MacroObservation> GeopoliticsAsync(CancellationToken ct)
    {
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        MacroObservation? rss = null;
        try
        {
            rss = ParseGeopoliticalRss(await client.GetStringAsync("https://feeds.bbci.co.uk/news/world/rss.xml", ct), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or System.Xml.XmlException or InvalidDataException) { }
        if (rss != null)
        {
            await store.WriteAsync("macro-geopolitics-v1", rss, ct);
            return rss;
        }
        const string source = "https://api.gdeltproject.org/api/v2/doc/doc?query=geopolitics%20OR%20war%20OR%20sanctions%20OR%20tariffs&mode=artlist&format=json&maxrecords=100&sort=datedesc";
        using var document = JsonDocument.Parse(await ProviderHttp.GetStringAsync(client, source, ct));
        var articles = document.RootElement.TryGetProperty("articles", out var items)
            ? items.EnumerateArray().ToArray()
            : [];
        if (articles.Length == 0) throw new InvalidDataException("Geopolitical feed has no articles.");
        var negative = new[] { "war", "attack", "conflict", "sanction", "tariff", "missile", "military", "crisis", "tension" };
        var negativeHits = articles.Count(article =>
        {
            var title = article.TryGetProperty("title", out var value) ? value.GetString() ?? "" : "";
            return negative.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase));
        });
        var riskScore = Math.Clamp(100m - negativeHits * 100m / articles.Length, 0m, 100m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new MacroObservation("GEO_RISK", today, riskScore, null, source);
        await store.WriteAsync("macro-geopolitics-v1", result, ct);
        return result;
    }

    public static MacroObservation ParseGeopoliticalRss(string xml, DateTimeOffset now)
    {
        var items = XDocument.Parse(xml).Descendants("item")
            .Select(x => new { Title = x.Element("title")?.Value.Trim(), Date = x.Element("pubDate")?.Value })
            .Where(x => !string.IsNullOrWhiteSpace(x.Title)
                && DateTimeOffset.TryParse(x.Date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
                && date <= now && date >= now.AddDays(-7))
            .DistinctBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Length < 5) throw new InvalidDataException("BBC geopolitical feed has insufficient recent headlines.");
        var pattern = @"\b(wars?|attacks?|conflicts?|sanctions?|tariffs?|missiles?|military|crisis|tensions?)\b";
        var hits = items.Count(x => System.Text.RegularExpressions.Regex.IsMatch(x.Title!, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant));
        var latest = items.Max(x => DateTimeOffset.Parse(x.Date!, CultureInfo.InvariantCulture));
        return new MacroObservation("GEO_RISK", DateOnly.FromDateTime(latest.UtcDateTime),
            100m - hits * 100m / items.Length, null, "https://feeds.bbci.co.uk/news/world/rss.xml");
    }

    public static MacroObservation ParseOfficialMacro(string series, string body, DateOnly today)
    {
        var points = new List<(DateOnly Date, decimal Value)>();
        string source;
        if (series == "UNRATE")
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.GetProperty("status").GetString() != "REQUEST_SUCCEEDED") throw new InvalidDataException("BLS request failed.");
            foreach (var item in json.RootElement.GetProperty("Results").GetProperty("series").EnumerateArray()
                .Where(s => s.GetProperty("seriesID").GetString() == "LNS14000000").SelectMany(s => s.GetProperty("data").EnumerateArray()))
            {
                var period = item.GetProperty("period").GetString()!;
                if (period.Length != 3 || period[0] != 'M' || !int.TryParse(period[1..], out var month) || month is < 1 or > 12) continue;
                if (!decimal.TryParse(item.GetProperty("value").GetString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var parsedValue)) continue;
                points.Add((new DateOnly(int.Parse(item.GetProperty("year").GetString()!, CultureInfo.InvariantCulture), month, 1), parsedValue));
            }
            source = "https://api.bls.gov/publicAPI/v2/timeseries/data/LNS14000000";
        }
        else
        {
            foreach (var entry in XDocument.Parse(body).Descendants().Where(e => e.Name.LocalName == "properties"))
            {
                var date = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "NEW_DATE")?.Value;
                var value = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "BC_10YEAR")?.Value;
                if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
                    points.Add((DateOnly.FromDateTime(parsedDate), parsedValue));
            }
            source = "https://home.treasury.gov/treasury-daily-interest-rate-xml-feed";
        }
        var sorted = points.Where(p => p.Date <= today).OrderBy(p => p.Date).ToArray();
        if (sorted.Length == 0) throw new InvalidDataException("Official macro feed has no observations.");
        var last = sorted[^1];
        var prior = sorted.Where(p => p.Date <= last.Date.AddMonths(-3)).ToArray();
        if (prior.Length == 0) throw new InvalidDataException("Official macro feed has no comparison period.");
        return new(series, last.Date, last.Value, prior[^1].Value, source);
    }
}
