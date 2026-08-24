using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Services;

public sealed class CotDataService
{
    private const string ApiBaseUrl = "https://publicreporting.cftc.gov/resource";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(10);
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);

    private readonly ApplicationDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CotDataService> _logger;

    public CotDataService(
        ApplicationDbContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<CotDataService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static IReadOnlyList<CotMarketDefinition> MarketDefinitions { get; } =
    [
        new("EUR/USD", "EUR/USD", "EURO FX", "gpe5-46if", "TFF Futures Only", "Leveraged Money", "lev_money_positions_long", "lev_money_positions_short"),
        new("XAU/USD", "XAU/USD", "GOLD", "72hh-3qpy", "Disaggregated Futures Only", "Managed Money", "m_money_positions_long", "m_money_positions_short"),
        new("NQ", "NASDAQ-100", "NASDAQ-100 Consolidated", "gpe5-46if", "TFF Futures Only", "Leveraged Money", "lev_money_positions_long", "lev_money_positions_short"),
        new("ES", "S&P 500", "E-MINI S&P 500", "gpe5-46if", "TFF Futures Only", "Leveraged Money", "lev_money_positions_long", "lev_money_positions_short")
    ];

    private static IReadOnlyList<CotMarketDefinition> LegacyMarketDefinitions { get; } =
    [
        new("EUR/USD", "EUR/USD", "EURO FX", "6dca-aqww", "Legacy Futures Only", "Commercials", "comm_positions_long_all", "comm_positions_short_all"),
        new("EUR/USD", "EUR/USD", "EURO FX", "6dca-aqww", "Legacy Futures Only", "Retailers", "nonrept_positions_long_all", "nonrept_positions_short_all"),
        new("XAU/USD", "XAU/USD", "GOLD", "6dca-aqww", "Legacy Futures Only", "Commercials", "comm_positions_long_all", "comm_positions_short_all"),
        new("XAU/USD", "XAU/USD", "GOLD", "6dca-aqww", "Legacy Futures Only", "Retailers", "nonrept_positions_long_all", "nonrept_positions_short_all"),
        new("NQ", "NASDAQ-100", "NASDAQ-100 Consolidated", "6dca-aqww", "Legacy Futures Only", "Commercials", "comm_positions_long_all", "comm_positions_short_all"),
        new("NQ", "NASDAQ-100", "NASDAQ-100 Consolidated", "6dca-aqww", "Legacy Futures Only", "Retailers", "nonrept_positions_long_all", "nonrept_positions_short_all"),
        new("ES", "S&P 500", "E-MINI S&P 500", "6dca-aqww", "Legacy Futures Only", "Commercials", "comm_positions_long_all", "comm_positions_short_all"),
        new("ES", "S&P 500", "E-MINI S&P 500", "6dca-aqww", "Legacy Futures Only", "Retailers", "nonrept_positions_long_all", "nonrept_positions_short_all")
    ];

    public async Task<CotUpdateResult> UpdateAsync(bool force, CancellationToken cancellationToken = default)
    {
        await UpdateLock.WaitAsync(cancellationToken);
        try
        {
            var lastSuccessfulRun = await _context.CotImportRuns
                .AsNoTracking()
                .Where(run => run.Success)
                .OrderByDescending(run => run.CompletedAt)
                .FirstOrDefaultAsync(cancellationToken);

        if (!force && lastSuccessfulRun?.CompletedAt is not null &&
            DateTimeOffset.UtcNow - lastSuccessfulRun.CompletedAt.Value < TimeSpan.FromHours(20))
            {
                return new CotUpdateResult(true, true, lastSuccessfulRun.RowsImported, "COT data je již aktuální.");
            }

            var importRun = new CotImportRun
            {
                StartedAt = DateTimeOffset.UtcNow,
                Message = "Aktualizace COT dat probíhá."
            };
            _context.CotImportRuns.Add(importRun);
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var parsedReports = new List<CotReport>();
                foreach (var definition in MarketDefinitions.Concat(LegacyMarketDefinitions))
                {
                    var rows = await FetchRowsAsync(definition, cancellationToken);
                    parsedReports.AddRange(BuildReports(definition, rows));
                }

                var imported = await UpsertReportsAsync(parsedReports, cancellationToken);
                importRun.Success = true;
                importRun.RowsImported = imported;
                importRun.CompletedAt = DateTimeOffset.UtcNow;
                importRun.Message = $"Importováno nebo aktualizováno {imported} COT záznamů.";
                await _context.SaveChangesAsync(cancellationToken);

                return new CotUpdateResult(true, false, imported, importRun.Message);
            }
            catch (Exception ex)
            {
                importRun.Success = false;
                importRun.CompletedAt = DateTimeOffset.UtcNow;
                importRun.Message = ex.Message[..Math.Min(ex.Message.Length, 2000)];
                await _context.SaveChangesAsync(CancellationToken.None);
                _logger.LogError(ex, "COT import failed.");
                throw;
            }
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    public async Task<CotDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var historicalReports = await _context.CotReports
            .AsNoTracking()
            .OrderByDescending(report => report.ReportDate)
            .ToListAsync(cancellationToken);

        var reportsBySymbolDate = historicalReports
            .GroupBy(report => (report.SymbolKey, report.ReportDate))
            .ToDictionary(group => group.Key, group => group.ToList());

        var historyBySymbol = MarketDefinitions.ToDictionary(
            definition => definition.SymbolKey,
            definition => (IReadOnlyList<CotChartPoint>)historicalReports
                .Where(report => report.SymbolKey == definition.SymbolKey && report.TraderCategory == definition.TraderCategory)
                .OrderByDescending(report => report.ReportDate)
                .Take(156)
                .OrderBy(report => report.ReportDate)
                .Select(report =>
                {
                    reportsBySymbolDate.TryGetValue((report.SymbolKey, report.ReportDate), out var sameDate);
                    return new CotChartPoint
                    {
                        ReportDate = report.ReportDate,
                        LongPosition = report.LongPosition,
                        ShortPosition = report.ShortPosition,
                        NetPosition = report.NetPosition,
                        CotIndex52 = report.CotIndex52,
                        CommercialsNetPosition = sameDate?.FirstOrDefault(item => item.TraderCategory == "Commercials")?.NetPosition,
                        RetailersNetPosition = sameDate?.FirstOrDefault(item => item.TraderCategory == "Retailers")?.NetPosition
                    };
                })
                .ToList());

        var latestBySymbol = MarketDefinitions.ToDictionary(
            definition => definition.SymbolKey,
            definition => historicalReports
                .Where(report => report.SymbolKey == definition.SymbolKey && report.TraderCategory == definition.TraderCategory)
                .OrderByDescending(report => report.ReportDate)
                .FirstOrDefault());

        var lastRun = await _context.CotImportRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new CotDashboardViewModel
        {
            LastSuccessfulImport = await _context.CotImportRuns
                .AsNoTracking()
                .Where(run => run.Success)
                .OrderByDescending(run => run.CompletedAt)
                .Select(run => run.CompletedAt)
                .FirstOrDefaultAsync(cancellationToken),
            LastImportMessage = lastRun?.Message,
            IsImportInProgress = lastRun is not null &&
                !lastRun.CompletedAt.HasValue &&
                DateTimeOffset.UtcNow - lastRun.StartedAt < TimeSpan.FromMinutes(30),
            Items = MarketDefinitions.Select(definition =>
            {
                latestBySymbol.TryGetValue(definition.SymbolKey, out var latest);
                historyBySymbol.TryGetValue(definition.SymbolKey, out var history);
                var commercials = historicalReports
                    .Where(report => report.SymbolKey == definition.SymbolKey && report.TraderCategory == "Commercials")
                    .OrderByDescending(report => report.ReportDate)
                    .FirstOrDefault();
                var retailers = historicalReports
                    .Where(report => report.SymbolKey == definition.SymbolKey && report.TraderCategory == "Retailers")
                    .OrderByDescending(report => report.ReportDate)
                    .FirstOrDefault();
                var isStale = IsStale(latest);
                return new CotDashboardItem
                {
                    SymbolKey = definition.SymbolKey,
                    DisplayName = definition.DisplayName,
                    Latest = latest,
                    TraderCategory = definition.TraderCategory,
                    IsStale = isStale,
                    StaleMessage = isStale
                        ? "COT report je starší než očekávaný týdenní interval. Aktualizuj data."
                        : string.Empty,
                    History = history ?? [],
                    Narrative = BuildNarrative(latest, commercials, retailers)
                };
            }).ToList()
        };
    }

    public CotContextViewModel? GetContextForMarket(string? market)
    {
        var symbolKey = NormalizeSymbol(market);
        if (symbolKey is null)
            return null;

        var latest = _context.CotReports
            .AsNoTracking()
            .Where(report => report.SymbolKey == symbolKey)
            .OrderByDescending(report => report.ReportDate)
            .FirstOrDefault();

        if (latest is null)
            return null;

        return new CotContextViewModel
        {
            SymbolKey = latest.SymbolKey,
            DisplayName = latest.DisplayName,
            Bias = latest.Bias,
            ReportDate = latest.ReportDate,
            NetPosition = latest.NetPosition,
            WeeklyNetChange = latest.WeeklyNetChange,
            Percentile52 = latest.Percentile52,
            ZScore = latest.ZScore,
            IsStale = IsStale(latest),
            Summary = latest.Summary
        };
    }

    public static string? NormalizeSymbol(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        return normalized switch
        {
            "EUR/USD" or "EURUSD" or "6E" or "M6E" or "EUROFX" or "EUROFXFUTURES" => "EUR/USD",
            "XAU/USD" or "XAUUSD" or "GC" or "MGC" or "GOLD" => "XAU/USD",
            "NQ" or "MNQ" or "NASDAQ-100" or "NASDAQ100" => "NQ",
            "ES" or "MES" or "S&P500" or "SP500" or "EMINISP500" => "ES",
            _ => null
        };
    }

    private async Task<List<JsonElement>> FetchRowsAsync(CotMarketDefinition definition, CancellationToken cancellationToken)
    {
        var where = $"contract_market_name = '{definition.MarketPattern.Replace("'", "''", StringComparison.Ordinal)}'";
        var url = $"{ApiBaseUrl}/{definition.DatasetId}.json?$limit=50000&$order=report_date_as_yyyy_mm_dd%20desc&$where={Uri.EscapeDataString(where)}";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingJournal-COT/1.0");

        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList()
            : [];
    }

    private static IEnumerable<CotReport> BuildReports(CotMarketDefinition definition, IEnumerable<JsonElement> rows)
    {
        var raw = rows
            .Select(row => ParseRawRow(definition, row))
            .Where(row => row is not null)
            .Select(row => row!)
            .GroupBy(row => row.ReportDate)
            .Select(group => group.First())
            .OrderBy(row => row.ReportDate)
            .ToList();

        for (var index = 0; index < raw.Count; index++)
        {
            var current = raw[index];
            var history = raw.Skip(Math.Max(0, index - 51)).Take(Math.Min(52, index + 1)).Select(row => (decimal)row.NetPosition).ToList();
            var history26 = history.TakeLast(Math.Min(26, history.Count)).ToList();
            var previous = index > 0 ? raw[index - 1] : null;
            var index52 = CalculateIndex(history);
            var index26 = CalculateIndex(history26);
            var percentile = CalculatePercentile(history, current.NetPosition);
            var zScore = CalculateZScore(history, current.NetPosition);
            var bias = CalculateBias(index52, current.NetPosition);

            yield return new CotReport
            {
                SymbolKey = definition.SymbolKey,
                DisplayName = definition.DisplayName,
                CftcMarketName = current.MarketName,
                CftcContractMarketCode = current.ContractMarketCode,
                SourceDataset = definition.SourceName,
                TraderCategory = definition.TraderCategory,
                ReportDate = current.ReportDate,
                LongPosition = current.LongPosition,
                ShortPosition = current.ShortPosition,
                NetPosition = current.NetPosition,
                WeeklyNetChange = previous is null ? null : current.NetPosition - previous.NetPosition,
                LongChange = previous is null ? null : current.LongPosition - previous.LongPosition,
                ShortChange = previous is null ? null : current.ShortPosition - previous.ShortPosition,
                OpenInterest = current.OpenInterest,
                CotIndex26 = index26,
                CotIndex52 = index52,
                Percentile52 = percentile,
                ZScore = zScore,
                ExtremeLong = index52 >= 90m,
                ExtremeShort = index52 <= 10m,
                Bias = bias,
                Summary = BuildSummary(bias, index52, current.NetPosition, current.OpenInterest)
            };
        }
    }

    private async Task<int> UpsertReportsAsync(IEnumerable<CotReport> reports, CancellationToken cancellationToken)
    {
        var materialized = reports.ToList();
        if (materialized.Count == 0)
            return 0;

        var symbols = materialized.Select(report => report.SymbolKey).Distinct().ToList();
        var dates = materialized.Select(report => report.ReportDate).Distinct().ToList();
        var existing = await _context.CotReports
            .Where(report => symbols.Contains(report.SymbolKey) && dates.Contains(report.ReportDate))
            .ToDictionaryAsync(report => GetUniqueKey(report), cancellationToken);

        foreach (var report in materialized)
        {
            if (existing.TryGetValue(GetUniqueKey(report), out var stored))
            {
                stored.DisplayName = report.DisplayName;
                stored.CftcMarketName = report.CftcMarketName;
                stored.CftcContractMarketCode = report.CftcContractMarketCode;
                stored.LongPosition = report.LongPosition;
                stored.ShortPosition = report.ShortPosition;
                stored.NetPosition = report.NetPosition;
                stored.WeeklyNetChange = report.WeeklyNetChange;
                stored.LongChange = report.LongChange;
                stored.ShortChange = report.ShortChange;
                stored.OpenInterest = report.OpenInterest;
                stored.CotIndex26 = report.CotIndex26;
                stored.CotIndex52 = report.CotIndex52;
                stored.Percentile52 = report.Percentile52;
                stored.ZScore = report.ZScore;
                stored.ExtremeLong = report.ExtremeLong;
                stored.ExtremeShort = report.ExtremeShort;
                stored.Bias = report.Bias;
                stored.Summary = report.Summary;
                stored.ImportedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _context.CotReports.Add(report);
                existing[GetUniqueKey(report)] = report;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return materialized.Count;
    }

    private static CotRawRow? ParseRawRow(CotMarketDefinition definition, JsonElement row)
    {
        var reportDate = GetDate(row, "report_date_as_yyyy_mm_dd", "report_date_as_mm_dd_yyyy");
        if (reportDate is null)
            return null;

        var longPosition = GetLong(row, definition.LongField, definition.LongField + "_all", "long_positions_all");
        var shortPosition = GetLong(row, definition.ShortField, definition.ShortField + "_all", "short_positions_all");
        return new CotRawRow(
            reportDate.Value,
            GetString(row, "contract_market_name", "CONTRACT_MARKET_NAME") ?? definition.MarketPattern,
            GetString(row, "cftc_contract_market_code") ?? string.Empty,
            longPosition,
            shortPosition,
            GetLong(row, "open_interest_all"));
    }

    private static string GetUniqueKey(CotReport report) =>
        $"{report.SymbolKey}|{report.ReportDate:yyyy-MM-dd}|{report.SourceDataset}|{report.TraderCategory}";

    private static bool IsStale(CotReport? report) =>
        report is null || DateTime.UtcNow.Date - report.ReportDate.ToDateTime(TimeOnly.MinValue).Date > StaleAfter;

    private static decimal? CalculateIndex(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
            return null;

        var min = values.Min();
        var max = values.Max();
        return max == min ? 50m : Math.Round((values[^1] - min) / (max - min) * 100m, 3);
    }

    private static decimal? CalculatePercentile(IReadOnlyList<decimal> values, long current)
    {
        if (values.Count == 0)
            return null;

        return Math.Round(values.Count(value => value <= current) / (decimal)values.Count * 100m, 3);
    }

    private static decimal? CalculateZScore(IReadOnlyList<decimal> values, long current)
    {
        if (values.Count < 2)
            return null;

        var average = values.Average();
        var variance = values.Sum(value => (value - average) * (value - average)) / values.Count;
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        return standardDeviation == 0 ? 0m : Math.Round((current - average) / standardDeviation, 4);
    }

    private static string CalculateBias(decimal? index52, long netPosition)
    {
        if (index52 >= 60m && netPosition > 0)
            return "Bullish";
        if (index52 <= 40m && netPosition < 0)
            return "Bearish";
        return "Neutral";
    }

    private static string BuildSummary(string bias, decimal? index52, long net, long openInterest) =>
        $"{bias} COT bias, čistá pozice {net:N0}, 52W index {(index52?.ToString("0", CultureInfo.InvariantCulture) ?? "-")} %, open interest {openInterest:N0}.";

    private static CotNarrative BuildNarrative(CotReport? primary, CotReport? commercials, CotReport? retailers)
    {
        if (primary is null)
            return new() { Headline = "COT shrnutí zatím není k dispozici." };

        var bullish = new List<string>();
        var warnings = new List<string>();
        var index = primary.CotIndex52;
        var bias = primary.Bias;

        if (index >= 80m)
            bullish.Add($"52W COT index je vysoko na {index:0.#} %, takže positioning je výrazně ve prospěch {bias.ToLowerInvariant()} scénáře.");
        else if (index <= 20m)
            warnings.Add($"52W COT index je nízko na {index:0.#} %, což ukazuje na výrazný opačný extrém.");

        if (primary.NetPosition > 0)
            bullish.Add($"Sledovaná hlavní skupina má čistou pozici long {primary.NetPosition:N0}.");
        else if (primary.NetPosition < 0)
            warnings.Add($"Sledovaná hlavní skupina má čistou pozici short {Math.Abs(primary.NetPosition):N0}.");

        if (commercials is not null)
        {
            var direction = commercials.NetPosition >= 0 ? "net long" : "net short";
            var argument = $"Commercials jsou {direction} {Math.Abs(commercials.NetPosition):N0}.";
            if ((commercials.NetPosition >= 0 && bias == "Bullish") || (commercials.NetPosition < 0 && bias == "Bearish"))
                bullish.Add(argument);
            else
                warnings.Add(argument);
        }

        if (retailers is not null)
        {
            var direction = retailers.NetPosition >= 0 ? "net long" : "net short";
            warnings.Add($"Retailers (Non-reportable) jsou {direction} {Math.Abs(retailers.NetPosition):N0}; tuto skupinu beru jako doplňkový, ne rozhodující signál.");
        }

        if (index >= 90m || index <= 10m)
            warnings.Add("COT index je v extrémní zóně, takže nejde o čerstvý začátek pohybu. Počítej i s rizikem vyčerpání nebo návratu k průměru.");

        if (bullish.Count == 0)
            bullish.Add("COT zatím neposkytuje silnou jednostrannou konfluenci.");
        if (warnings.Count == 0)
            warnings.Add("COT neukazuje výrazný rozpor, přesto sám o sobě není vstupním signálem.");

        var side = bias switch
        {
            "Bullish" => "spíše bullish",
            "Bearish" => "spíše bearish",
            _ => "neutrální"
        };
        var headline = $"{primary.DisplayName}: COT bias {side}.";
        var overview = $"Podle posledního reportu z {primary.ReportDate:dd.MM.yyyy} má trh {side} weekly positioning. COT pomáhá určit preferovaný směr, ale neříká, kdy vstoupit do tradu.";
        var practical = bias switch
        {
            "Bullish" => "Long setup má z COT větší podporu než short. Vstup ale čekej až po potvrzení Můčkovou technikou: HTF kontext, výběr liquidity, demand nebo FVG/OB reakce, structure shift, killzone a přijatelné RRR.",
            "Bearish" => "Short setup má z COT větší podporu než long. Vstup ale čekej až po potvrzení Můčkovou technikou: HTF kontext, výběr liquidity, supply nebo FVG/OB reakce, structure shift, killzone a přijatelné RRR.",
            _ => "COT nedává jasnou preferenci. Směr nech potvrdit Můčkovou technikou: HTF kontext, liquidity, supply/demand, FVG/OB, structure shift, killzone a přijatelné RRR."
        };

        return new()
        {
            Headline = headline,
            Overview = overview,
            BullishArguments = bullish,
            WarningArguments = warnings,
            PracticalUse = practical
        };
    }

    private static string? GetString(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null)
                return property.ToString();
        }

        return null;
    }

    private static long GetLong(JsonElement row, params string[] names)
    {
        var value = GetString(row, names);
        return long.TryParse(value?.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static DateOnly? GetDate(JsonElement row, params string[] names)
    {
        var value = GetString(row, names);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateTime))
            return DateOnly.FromDateTime(dateTime.UtcDateTime);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly;
        return null;
    }

    private sealed record CotRawRow(
        DateOnly ReportDate,
        string MarketName,
        string ContractMarketCode,
        long LongPosition,
        long ShortPosition,
        long OpenInterest)
    {
        public long NetPosition => LongPosition - ShortPosition;
    }
}

public sealed record CotMarketDefinition(
    string SymbolKey,
    string DisplayName,
    string MarketPattern,
    string DatasetId,
    string SourceName,
    string TraderCategory,
    string LongField,
    string ShortField);

public sealed record CotUpdateResult(bool Success, bool Skipped, int RowsImported, string Message);
