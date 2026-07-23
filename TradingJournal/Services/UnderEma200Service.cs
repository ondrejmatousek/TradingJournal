using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Services
{
    public class UnderEma200Service
    {
        private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromHours(12);
        private static readonly SemaphoreSlim RefreshLock = new(1, 1);

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

        private readonly ApplicationDbContext _context;
        private readonly IUnderEma200HistoryProvider _historyProvider;
        private readonly ILogger<UnderEma200Service> _logger;

        public UnderEma200Service(
            ApplicationDbContext context,
            IUnderEma200HistoryProvider historyProvider,
            ILogger<UnderEma200Service> logger)
        {
            _context = context;
            _historyProvider = historyProvider;
            _logger = logger;
        }

        public async Task<UnderEma200Result> GetAsync(string? timeframeValue = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var timeframe = EmaTimeframeParser.Parse(timeframeValue);

            if (!forceRefresh)
            {
                var persisted = await GetPersistedResultAsync(timeframe, cancellationToken);
                if (persisted.Items.Count > 0 && persisted.UpdatedAt is not null &&
                    DateTimeOffset.UtcNow - persisted.UpdatedAt.Value < SnapshotLifetime)
                {
                    return persisted;
                }
            }

            await RefreshLock.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh)
                {
                    var persisted = await GetPersistedResultAsync(timeframe, cancellationToken);
                    if (persisted.Items.Count > 0 && persisted.UpdatedAt is not null &&
                        DateTimeOffset.UtcNow - persisted.UpdatedAt.Value < SnapshotLifetime)
                    {
                        return persisted;
                    }
                }

                return await RefreshAsync(timeframe, cancellationToken);
            }
            finally
            {
                RefreshLock.Release();
            }
        }

        public Task<UnderEma200Result> GetAsync(bool forceRefresh, CancellationToken cancellationToken = default)
        {
            return GetAsync(EmaTimeframe.Daily.ToString(), forceRefresh, cancellationToken);
        }

        public Task<UnderEma200Result> GetPersistedResultAsync(CancellationToken cancellationToken)
        {
            return GetPersistedResultAsync(EmaTimeframe.Daily, cancellationToken);
        }

        public Task<UnderEma200Result> GetPersistedResultAsync(string? timeframeValue, CancellationToken cancellationToken = default)
        {
            return GetPersistedResultAsync(EmaTimeframeParser.Parse(timeframeValue), cancellationToken);
        }

        public async Task<UnderEma200Result> GetPersistedResultAsync(EmaTimeframe timeframe = EmaTimeframe.Daily, CancellationToken cancellationToken = default)
        {
            var snapshots = await LoadSnapshotsAsync(timeframe, cancellationToken);
            return BuildResult(timeframe, snapshots, snapshots.Count > 0
                ? "Nacteno z posledniho ulozeneho screeningu."
                : "Tabulka InvestmentEma200Snapshots zatim neobsahuje zadny snapshot. Spust Refresh pro prvni screening.");
        }

        public Task<UnderEma200Result> RefreshAsync(string? timeframeValue, CancellationToken cancellationToken = default)
        {
            return RefreshAsync(EmaTimeframeParser.Parse(timeframeValue), cancellationToken);
        }

        public Task<UnderEma200Result> RefreshAsync(CancellationToken cancellationToken)
        {
            return RefreshAsync(EmaTimeframe.Daily, cancellationToken);
        }

        public async Task<UnderEma200Result> RefreshAsync(EmaTimeframe timeframe = EmaTimeframe.Daily, CancellationToken cancellationToken = default)
        {
            var connection = _context.Database.GetDbConnection();
            _logger.LogInformation(
                "EMA200 {Timeframe} refresh uses database {DataSource}/{Database}.",
                timeframe,
                connection.DataSource,
                connection.Database);

            var stats = new RefreshStats(InputTickers: Candidates.Length);
            _logger.LogInformation("EMA200 {Timeframe} refresh started for {InputTickers} tickers.", timeframe, stats.InputTickers);

            using var throttle = new SemaphoreSlim(3, 3);
            var tasks = Candidates.Select(async candidate =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return await AnalyzeCandidateAsync(candidate, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                }
            });

            var analyses = (await Task.WhenAll(tasks)).ToList();
            var selected = analyses
                .Select(analysis => UnderEma200Calculator.EvaluateSnapshot(analysis, timeframe))
                .ToList();
            stats = stats with
            {
                DownloadedHistories = analyses.Count(item => item.DailyPriceCount > 0 && item.ErrorMessage is null),
                AtLeast200Candles = analyses.Count(item => timeframe == EmaTimeframe.Weekly
                    ? item.WeeklyEmaSourceCount >= UnderEma200Calculator.EmaPeriod
                    : item.DailyEmaSourceCount >= UnderEma200Calculator.EmaPeriod),
                BelowEma200 = selected.Count(item => item.IsBelowEma200),
                Qualified = selected.Count(item => item.IsQualified),
                Unqualified = selected.Count(item => !item.IsQualified),
                Errors = analyses.Count(item => !string.IsNullOrWhiteSpace(item.ErrorMessage)),
                CreatedSnapshots = analyses.Count
            };

            if (stats.DownloadedHistories == 0)
            {
                _logger.LogWarning(
                    "EMA200 refresh produced zero usable histories. Existing snapshot remains unchanged. {@RefreshStats}",
                    stats);

                var persisted = await GetPersistedResultAsync(timeframe, cancellationToken);
                persisted.Message = "Refresh nestahl zadnou pouzitelnou historii. Puvodni snapshot zustal zachovany; detaily hledej v logu EMA200 refresh.";
                return persisted;
            }

            var affectedRows = await PersistSnapshotsAsync(analyses, cancellationToken);
            stats = stats with { SaveChangesRows = affectedRows };

            _logger.LogInformation(
                "EMA200 refresh finished. {@RefreshStats}",
                stats);

            var snapshots = await LoadSnapshotsAsync(timeframe, cancellationToken);
            var result = BuildResult(timeframe, snapshots, $"Screening dokoncen. Ulozeno {snapshots.Count} snapshotu. Hodnoty pro {EmaTimeframeParser.ToCompactLabel(timeframe)} se dopocitaji pri zobrazeni.");
            result.Message = $"{result.Message} Pod vybranou EMA je {result.BelowEmaCount} akcii.";
            return result;
        }

        private async Task<IReadOnlyList<InvestmentEma200Snapshot>> LoadSnapshotsAsync(EmaTimeframe timeframe, CancellationToken cancellationToken)
        {
            var snapshots = await _context.InvestmentEma200Snapshots
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return snapshots
                .OrderByDescending(snapshot => UnderEma200Calculator.EvaluateSnapshot(snapshot, timeframe).IsBelowEma200)
                .ThenByDescending(snapshot => UnderEma200Calculator.EvaluateSnapshot(snapshot, timeframe).IsQualified)
                .ThenByDescending(snapshot => UnderEma200Calculator.EvaluateSnapshot(snapshot, timeframe).Score)
                .ThenBy(snapshot => UnderEma200Calculator.EvaluateSnapshot(snapshot, timeframe).DistanceFromEmaPercent)
                .ToList();
        }

        private async Task<UnderEma200SnapshotMetrics> AnalyzeCandidateAsync(StockCandidate candidate, CancellationToken cancellationToken)
        {
            try
            {
                var prices = await _historyProvider.GetDailyPricesAsync(candidate.Symbol, cancellationToken);
                if (prices.Count == 0)
                {
                    _logger.LogWarning("EMA200 provider returned zero daily candles for {Symbol}.", candidate.Symbol);
                    return CreateDiagnosticAnalysis(candidate, "nedostatek historickych dat", null, 0);
                }

                var analysis = UnderEma200Calculator.CalculateSnapshotMetrics(prices, candidate.Symbol, candidate.Name, candidate.Sector, "Yahoo / Alpha Vantage / Stooq");
                _logger.LogInformation(
                    "EMA200 analyzed {Symbol}: dailyCandles={DailyPriceCount}, weeklyCandles={WeeklyEmaSourceCount}, dailyEma={DailyEma}, weeklyEma={WeeklyEma}, error={ErrorMessage}.",
                    analysis.Symbol,
                    analysis.DailyPriceCount,
                    analysis.WeeklyEmaSourceCount,
                    analysis.Ema200Daily,
                    analysis.Ema200Weekly,
                    analysis.ErrorMessage);

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EMA200 refresh failed for ticker {Symbol}.", candidate.Symbol);
                return CreateDiagnosticAnalysis(candidate, "chyba externiho poskytovatele dat", ex.Message, 0);
            }
        }

        private async Task<int> PersistSnapshotsAsync(IReadOnlyList<UnderEma200SnapshotMetrics> analyses, CancellationToken cancellationToken)
        {
            if (analyses.Count == 0)
            {
                _logger.LogWarning("EMA200 refresh produced zero snapshots. Existing snapshot remains unchanged.");
                return 0;
            }

            var now = DateTimeOffset.UtcNow;
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                var oldSnapshots = await _context.InvestmentEma200Snapshots
                    .ToListAsync(cancellationToken);
                _context.InvestmentEma200Snapshots.RemoveRange(oldSnapshots);

                var snapshots = analyses.Select(analysis => new InvestmentEma200Snapshot
                {
                    Symbol = analysis.Symbol,
                    Name = analysis.Name,
                    Sector = analysis.Sector,
                    CurrentPrice = analysis.CurrentPrice,
                    Ema200Daily = analysis.Ema200Daily,
                    Ema200Weekly = analysis.Ema200Weekly,
                    Performance1YearPercent = analysis.Performance1YearPercent,
                    Performance3YearPercent = analysis.Performance3YearPercent,
                    Performance5YearPercent = analysis.Performance5YearPercent,
                    DailyPriceCount = analysis.DailyPriceCount,
                    DailyEmaSourceCount = analysis.DailyEmaSourceCount,
                    WeeklyEmaSourceCount = analysis.WeeklyEmaSourceCount,
                    ErrorMessage = analysis.ErrorMessage,
                    LastPriceDate = analysis.LastPriceDate,
                    CalculationDate = analysis.CalculationDate,
                    UpdatedAt = now,
                    SnapshotCreatedAtUtc = now.UtcDateTime,
                    DataSource = analysis.DataSource
                }).ToList();

                await _context.InvestmentEma200Snapshots.AddRangeAsync(snapshots, cancellationToken);

                var affectedRows = await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "EMA200 snapshots persisted. old={OldSnapshots}, new={NewSnapshots}, affectedRows={AffectedRows}.",
                    oldSnapshots.Count,
                    snapshots.Count,
                    affectedRows);

                return affectedRows;
            });
        }

        private static UnderEma200SnapshotMetrics CreateDiagnosticAnalysis(StockCandidate candidate, string rejectionReason, string? errorMessage, int dailyPriceCount)
        {
            return new UnderEma200SnapshotMetrics(
                Symbol: candidate.Symbol,
                Name: candidate.Name,
                Sector: candidate.Sector,
                CurrentPrice: 0m,
                Ema200Daily: 0m,
                Ema200Weekly: 0m,
                Performance1YearPercent: 0m,
                Performance3YearPercent: 0m,
                Performance5YearPercent: 0m,
                ErrorMessage: errorMessage ?? rejectionReason,
                DailyPriceCount: dailyPriceCount,
                DailyEmaSourceCount: 0,
                WeeklyEmaSourceCount: 0,
                LastPriceDate: DateTime.MinValue,
                CalculationDate: DateTime.UtcNow,
                DataSource: "Yahoo / Alpha Vantage / Stooq");
        }

        private static UnderEma200Result BuildResult(EmaTimeframe timeframe, IReadOnlyList<InvestmentEma200Snapshot> snapshots, string message)
        {
            return new UnderEma200Result
            {
                UpdatedAt = snapshots.Count == 0 ? null : snapshots.Max(snapshot => snapshot.UpdatedAt),
                Message = message,
                Timeframe = timeframe.ToString(),
                TimeframeLabel = EmaTimeframeParser.ToLabel(timeframe),
                EmaLabel = EmaTimeframeParser.ToCompactLabel(timeframe),
                Items = snapshots
                    .Select(snapshot =>
                    {
                        var computed = UnderEma200Calculator.EvaluateSnapshot(snapshot, timeframe);
                        return new UnderEma200StockCard
                    {
                        Symbol = snapshot.Symbol,
                        Name = snapshot.Name,
                        Sector = snapshot.Sector,
                        Timeframe = timeframe.ToString(),
                        CurrentPrice = snapshot.CurrentPrice,
                        Ema200 = computed.Ema200,
                        DistanceFromEmaPercent = computed.DistanceFromEmaPercent,
                        Performance1YearPercent = snapshot.Performance1YearPercent,
                        Performance3YearPercent = snapshot.Performance3YearPercent,
                        Performance5YearPercent = snapshot.Performance5YearPercent,
                        Score = computed.Score,
                        IsBelowEma200 = computed.IsBelowEma200,
                        IsQualified = computed.IsQualified,
                        FailureReasons = string.Join("; ", computed.FailureReasons),
                        RejectionReason = computed.RejectionReason,
                        ErrorMessage = snapshot.ErrorMessage,
                        LastPriceDate = snapshot.LastPriceDate,
                        CalculationDate = snapshot.CalculationDate,
                        SnapshotCreatedAtUtc = snapshot.SnapshotCreatedAtUtc,
                        DataSource = snapshot.DataSource,
                        ChartUrl = $"https://www.tradingview.com/chart/?symbol={snapshot.Symbol}",
                        Rationale = BuildRationale(snapshot, computed)
                    };
                    })
                    .ToList()
            };
        }

        private static string BuildRationale(InvestmentEma200Snapshot snapshot, UnderEma200ComputedResult computed)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                return $"Provider dat vratil chybu: {snapshot.ErrorMessage}";
            }

            if (!computed.IsBelowEma200)
            {
                return computed.RejectionReason ?? $"Akcie neni pod {EmaTimeframeParser.ToCompactLabel(computed.Timeframe)}.";
            }

            var distance = Math.Abs(computed.DistanceFromEmaPercent).ToString("0.#", CultureInfo.InvariantCulture);
            var fiveYear = snapshot.Performance5YearPercent.ToString("0.#", CultureInfo.InvariantCulture);
            var threeYear = snapshot.Performance3YearPercent.ToString("0.#", CultureInfo.InvariantCulture);

            return computed.IsQualified
                ? $"Cena je {distance} % pod {EmaTimeframeParser.ToCompactLabel(computed.Timeframe)}, 5Y vykon {fiveYear} % a 3Y vykon {threeYear} %. Splnuje dlouhodoby rustovy filtr."
                : $"Cena je {distance} % pod {EmaTimeframeParser.ToCompactLabel(computed.Timeframe)}, ale filtr selhal: {computed.RejectionReason ?? string.Join("; ", computed.FailureReasons)}.";
        }

        private sealed record StockCandidate(string Symbol, string Name, string Sector);

        private sealed record RefreshStats(
            int InputTickers,
            int DownloadedHistories = 0,
            int AtLeast200Candles = 0,
            int BelowEma200 = 0,
            int Qualified = 0,
            int Unqualified = 0,
            int Errors = 0,
            int CreatedSnapshots = 0,
            int SaveChangesRows = 0);
    }
}
