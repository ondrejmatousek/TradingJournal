using TradingJournal.ViewModels;
using TradingJournal.Models;

namespace TradingJournal.Services
{
    public static class UnderEma200Calculator
    {
        public const int EmaPeriod = 200;
        public const int OneYearTradingDays = 252;
        public const int ThreeYearTradingDays = 252 * 3;
        public const int FiveYearTradingDays = 252 * 5;

        public static IReadOnlyList<decimal> CalculateEma(IReadOnlyList<decimal> values, int period)
        {
            if (values.Count == 0)
            {
                return [];
            }

            if (period <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "EMA period must be positive.");
            }

            var ema = new List<decimal>(values.Count);
            var multiplier = 2m / (period + 1);
            var seed = values.Take(Math.Min(period, values.Count)).Average();

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

        public static decimal CalculatePerformance(decimal current, decimal past)
        {
            if (past <= 0)
            {
                return 0;
            }

            return (current - past) / past * 100m;
        }

        public static UnderEma200Analysis Analyze(
            IReadOnlyList<UnderEma200PricePoint> dailyPrices,
            string symbol,
            string name,
            string sector,
            string dataSource,
            EmaTimeframe timeframe = EmaTimeframe.Daily)
        {
            var snapshot = CalculateSnapshotMetrics(dailyPrices, symbol, name, sector, dataSource);
            var computed = EvaluateSnapshot(snapshot, timeframe);

            return new UnderEma200Analysis(
                Symbol: snapshot.Symbol,
                Name: snapshot.Name,
                Sector: snapshot.Sector,
                Timeframe: timeframe,
                CurrentPrice: snapshot.CurrentPrice,
                Ema200: computed.Ema200,
                DistanceFromEmaPercent: computed.DistanceFromEmaPercent,
                Performance1YearPercent: snapshot.Performance1YearPercent,
                Performance3YearPercent: snapshot.Performance3YearPercent,
                Performance5YearPercent: snapshot.Performance5YearPercent,
                Score: computed.Score,
                IsBelowEma200: computed.IsBelowEma200,
                IsQualified: computed.IsQualified,
                FailureReasons: computed.FailureReasons,
                RejectionReason: computed.RejectionReason,
                ErrorMessage: snapshot.ErrorMessage,
                DailyPriceCount: snapshot.DailyPriceCount,
                LastPriceDate: snapshot.LastPriceDate,
                CalculationDate: snapshot.CalculationDate,
                DataSource: snapshot.DataSource);
        }

        public static UnderEma200SnapshotMetrics CalculateSnapshotMetrics(
            IReadOnlyList<UnderEma200PricePoint> dailyPrices,
            string symbol,
            string name,
            string sector,
            string dataSource,
            string? errorMessage = null)
        {
            var orderedPrices = dailyPrices
                .Where(point => point.Close > 0)
                .OrderBy(point => point.Date)
                .ToList();

            var latest = orderedPrices.LastOrDefault();
            var currentPrice = latest?.Close ?? 0m;
            var weeklyPrices = BuildEmaSourcePrices(orderedPrices, EmaTimeframe.Weekly);
            var dailyEma = CalculateLastEma(orderedPrices);
            var weeklyEma = CalculateLastEma(weeklyPrices);

            var oneYearPerformance = orderedPrices.Count >= OneYearTradingDays
                ? CalculatePerformance(currentPrice, orderedPrices[^OneYearTradingDays].Close)
                : 0m;
            var threeYearPerformance = orderedPrices.Count >= ThreeYearTradingDays
                ? CalculatePerformance(currentPrice, orderedPrices[^ThreeYearTradingDays].Close)
                : 0m;
            var fiveYearPerformance = orderedPrices.Count >= FiveYearTradingDays
                ? CalculatePerformance(currentPrice, orderedPrices[^FiveYearTradingDays].Close)
                : 0m;

            return new UnderEma200SnapshotMetrics(
                Symbol: symbol,
                Name: name,
                Sector: sector,
                CurrentPrice: currentPrice,
                Ema200Daily: dailyEma,
                Ema200Weekly: weeklyEma,
                Performance1YearPercent: oneYearPerformance,
                Performance3YearPercent: threeYearPerformance,
                Performance5YearPercent: fiveYearPerformance,
                DailyPriceCount: orderedPrices.Count,
                DailyEmaSourceCount: orderedPrices.Count,
                WeeklyEmaSourceCount: weeklyPrices.Count,
                ErrorMessage: errorMessage,
                LastPriceDate: latest?.Date ?? DateTime.MinValue,
                CalculationDate: DateTime.UtcNow,
                DataSource: dataSource);
        }

        public static UnderEma200ComputedResult EvaluateSnapshot(
            InvestmentEma200Snapshot snapshot,
            EmaTimeframe timeframe)
        {
            return EvaluateSnapshot(
                new UnderEma200SnapshotMetrics(
                    snapshot.Symbol,
                    snapshot.Name,
                    snapshot.Sector,
                    snapshot.CurrentPrice,
                    snapshot.Ema200Daily,
                    snapshot.Ema200Weekly,
                    snapshot.Performance1YearPercent,
                    snapshot.Performance3YearPercent,
                    snapshot.Performance5YearPercent,
                    snapshot.DailyPriceCount,
                    snapshot.DailyEmaSourceCount,
                    snapshot.WeeklyEmaSourceCount,
                    snapshot.ErrorMessage,
                    snapshot.LastPriceDate,
                    snapshot.CalculationDate,
                    snapshot.DataSource),
                timeframe);
        }

        public static UnderEma200ComputedResult EvaluateSnapshot(
            UnderEma200SnapshotMetrics snapshot,
            EmaTimeframe timeframe)
        {
            var ema200 = timeframe == EmaTimeframe.Weekly ? snapshot.Ema200Weekly : snapshot.Ema200Daily;
            var sourceCount = timeframe == EmaTimeframe.Weekly ? snapshot.WeeklyEmaSourceCount : snapshot.DailyEmaSourceCount;
            var distanceFromEma = ema200 > 0m ? (snapshot.CurrentPrice - ema200) / ema200 * 100m : 0m;
            var isBelowEma = ema200 > 0m && snapshot.CurrentPrice < ema200;
            var qualification = EvaluateQualification(
                isBelowEma,
                timeframe,
                snapshot.Performance3YearPercent,
                snapshot.Performance5YearPercent,
                distanceFromEma,
                snapshot.DailyPriceCount,
                sourceCount);
            var score = CalculateScore(
                distanceFromEma,
                snapshot.Performance1YearPercent,
                snapshot.Performance3YearPercent,
                snapshot.Performance5YearPercent);

            return new UnderEma200ComputedResult(
                Timeframe: timeframe,
                Ema200: ema200,
                DistanceFromEmaPercent: distanceFromEma,
                Score: score,
                IsBelowEma200: isBelowEma,
                IsQualified: qualification.IsQualified,
                FailureReasons: qualification.FailureReasons,
                RejectionReason: qualification.IsQualified ? null : string.Join("; ", qualification.FailureReasons));
        }

        public static QualificationResult EvaluateQualification(
            bool isBelowEma200,
            EmaTimeframe timeframe,
            decimal performance3YearPercent,
            decimal performance5YearPercent,
            decimal distanceFromEmaPercent,
            int dailyPriceCount,
            int? emaSourceCount = null)
        {
            var failures = new List<string>();

            if (dailyPriceCount < FiveYearTradingDays)
            {
                failures.Add("mene nez 5 let daily historie");
            }

            if ((emaSourceCount ?? EmaPeriod) < EmaPeriod)
            {
                failures.Add(timeframe == EmaTimeframe.Weekly
                    ? "nedostatek dokoncenych weekly candles pro EMA200"
                    : "nedostatek daily candles pro EMA200");
            }

            if (!isBelowEma200)
            {
                failures.Add($"cena neni pod {EmaTimeframeParser.ToCompactLabel(timeframe)}");
            }

            if (performance5YearPercent < 20m)
            {
                failures.Add("5Y vykon je pod +20 %");
            }

            if (performance3YearPercent <= 0)
            {
                failures.Add("3Y vykon neni pozitivni");
            }

            if (distanceFromEmaPercent < -35m)
            {
                failures.Add("cena je vice nez 35 % pod EMA200");
            }

            return new QualificationResult(failures.Count == 0, failures);
        }

        private static decimal CalculateLastEma(IReadOnlyList<UnderEma200PricePoint> prices)
        {
            if (prices.Count < EmaPeriod)
            {
                return 0m;
            }

            var ema = CalculateEma(prices.Select(point => point.Close).ToList(), EmaPeriod);
            return ema[^1];
        }

        public static int CalculateScore(
            decimal distanceFromEmaPercent,
            decimal performance1YearPercent,
            decimal performance3YearPercent,
            decimal performance5YearPercent)
        {
            var distanceDepth = Math.Abs(distanceFromEmaPercent);
            var distanceScore = distanceDepth switch
            {
                >= 5m and <= 20m => 30m,
                > 20m and <= 35m => 30m - ((distanceDepth - 20m) / 15m * 15m),
                > 0m and < 5m => 15m + (distanceDepth / 5m * 15m),
                _ => 0m
            };

            var fiveYearScore = Clamp(performance5YearPercent / 150m * 35m, 0m, 35m);
            var threeYearScore = Clamp(performance3YearPercent / 80m * 25m, 0m, 25m);
            var oneYearScore = Clamp((performance1YearPercent + 20m) / 80m * 10m, 0m, 10m);

            return (int)Math.Round(Clamp(distanceScore + fiveYearScore + threeYearScore + oneYearScore, 0m, 100m), MidpointRounding.AwayFromZero);
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        public static IReadOnlyList<UnderEma200PricePoint> BuildEmaSourcePrices(
            IReadOnlyList<UnderEma200PricePoint> orderedDailyPrices,
            EmaTimeframe timeframe)
        {
            if (orderedDailyPrices.Count == 0)
            {
                return [];
            }

            if (timeframe == EmaTimeframe.Daily)
            {
                return orderedDailyPrices;
            }

            var currentWeekStart = GetWeekStart(orderedDailyPrices[^1].Date);

            return orderedDailyPrices
                .Where(point => point.Date.Date < currentWeekStart)
                .GroupBy(point => GetWeekStart(point.Date))
                .Select(group => group.OrderBy(point => point.Date).Last())
                .OrderBy(point => point.Date)
                .ToList();
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var dayOffset = ((int)date.Date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-dayOffset);
        }
    }

    public sealed record UnderEma200PricePoint(DateTime Date, decimal Close);

    public sealed record QualificationResult(bool IsQualified, IReadOnlyList<string> FailureReasons);

    public sealed record UnderEma200Analysis(
        string Symbol,
        string Name,
        string Sector,
        EmaTimeframe Timeframe,
        decimal CurrentPrice,
        decimal Ema200,
        decimal DistanceFromEmaPercent,
        decimal Performance1YearPercent,
        decimal Performance3YearPercent,
        decimal Performance5YearPercent,
        int Score,
        bool IsBelowEma200,
        bool IsQualified,
        IReadOnlyList<string> FailureReasons,
        string? RejectionReason,
        string? ErrorMessage,
        int DailyPriceCount,
        DateTime LastPriceDate,
        DateTime CalculationDate,
        string DataSource);

    public sealed record UnderEma200SnapshotMetrics(
        string Symbol,
        string Name,
        string Sector,
        decimal CurrentPrice,
        decimal Ema200Daily,
        decimal Ema200Weekly,
        decimal Performance1YearPercent,
        decimal Performance3YearPercent,
        decimal Performance5YearPercent,
        int DailyPriceCount,
        int DailyEmaSourceCount,
        int WeeklyEmaSourceCount,
        string? ErrorMessage,
        DateTime LastPriceDate,
        DateTime CalculationDate,
        string DataSource);

    public sealed record UnderEma200ComputedResult(
        EmaTimeframe Timeframe,
        decimal Ema200,
        decimal DistanceFromEmaPercent,
        int Score,
        bool IsBelowEma200,
        bool IsQualified,
        IReadOnlyList<string> FailureReasons,
        string? RejectionReason);
}
