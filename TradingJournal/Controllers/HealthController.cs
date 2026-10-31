using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Diagnostics;
using TradingJournal.Data;
using TradingJournal.Models;

namespace TradingJournal.Controllers
{
    [AllowAnonymous]
    [ApiController]
    [Route("health")]
    public class HealthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HealthController> _logger;

        public HealthController(ApplicationDbContext context, ILogger<HealthController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("live")]
        [HttpHead("live")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Live()
        {
            return Ok(new
            {
                status = "Healthy",
                checkedAt = DateTimeOffset.UtcNow
            });
        }

        [HttpGet("ready")]
        public async Task<IActionResult> Ready(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var checks = new List<ReadinessCheck>();

            await AddDatabaseConnectivityCheckAsync(checks, cancellationToken);

            if (checks.Any(check => !check.Healthy))
                return ReadinessResponse(checks, stopwatch);

            await AddMigrationCheckAsync(checks, cancellationToken);
            await AddSchemaChecksAsync(checks, cancellationToken);

            return ReadinessResponse(checks, stopwatch);
        }

        [HttpGet("deep")]
        public async Task<IActionResult> Deep(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var checks = new List<ReadinessCheck>();

            await AddDatabaseConnectivityCheckAsync(checks, cancellationToken);

            if (checks.Any(check => !check.Healthy))
                return ReadinessResponse(checks, stopwatch);

            await AddMigrationCheckAsync(checks, cancellationToken);
            await AddSchemaChecksAsync(checks, cancellationToken);

            if (checks.All(check => check.Healthy))
            {
                await AddJournalWriteSmokeCheckAsync(checks, cancellationToken);
                await AddSetupReviewWriteSmokeCheckAsync(checks, cancellationToken);
            }

            return ReadinessResponse(checks, stopwatch);
        }

        private async Task AddDatabaseConnectivityCheckAsync(List<ReadinessCheck> checks, CancellationToken cancellationToken)
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                checks.Add(new ReadinessCheck("database-connectivity", canConnect, canConnect ? "Database connection OK." : "Database connection failed."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness database connectivity check failed.");
                checks.Add(new ReadinessCheck("database-connectivity", false, ex.Message));
            }
        }

        private async Task AddMigrationCheckAsync(List<ReadinessCheck> checks, CancellationToken cancellationToken)
        {
            try
            {
                var pendingMigrations = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                checks.Add(new ReadinessCheck(
                    "ef-migrations",
                    pendingMigrations.Count == 0,
                    pendingMigrations.Count == 0
                        ? "No pending EF migrations."
                        : $"Pending EF migrations: {string.Join(", ", pendingMigrations)}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness migration check failed.");
                checks.Add(new ReadinessCheck("ef-migrations", false, ex.Message));
            }
        }

        private async Task AddSchemaChecksAsync(List<ReadinessCheck> checks, CancellationToken cancellationToken)
        {
            var requiredTables = new[]
            {
                "AspNetUsers",
                "JournalEntries",
                "TradingJournals",
                "MarketPrices",
                "JournalEntryImages",
                "InvoiceCompanies",
                "Invoices",
                "InvoiceItems",
                "MarketBiases",
                "StrategyNotes",
                "TradeSetupReviews",
                "TradeSetupReviewImages",
                "ScreeningDocuments"
            };

            foreach (var table in requiredTables)
            {
                var exists = await TableExistsAsync(table, cancellationToken);
                checks.Add(new ReadinessCheck($"table:{table}", exists, exists ? "Table exists." : $"Missing table dbo.{table}."));
            }

            var requiredColumns = new[]
            {
                ("JournalEntries", "UserId"),
                ("JournalEntries", "JournalId"),
                ("TradingJournals", "UserId"),
                ("TradingJournals", "Name"),
                ("MarketPrices", "AssetKey"),
                ("MarketPrices", "Date"),
                ("MarketPrices", "Close"),
                ("JournalEntries", "EntryTime"),
                ("JournalEntries", "ExitTime"),
                ("JournalEntries", "ExitPrice"),
                ("JournalEntries", "Status"),
                ("JournalEntries", "ImageData"),
                ("JournalEntries", "ImageContentType"),
                ("JournalEntries", "ImageFileName"),
                ("JournalEntryImages", "JournalEntryId"),
                ("JournalEntryImages", "ImageData"),
                ("JournalEntryImages", "ImageContentType"),
                ("JournalEntryImages", "ImageFileName"),
                ("JournalEntryImages", "CreatedAt"),
                ("InvoiceCompanies", "LastHourlyRate"),
                ("InvoiceCompanies", "IsVatPayer"),
                ("Invoices", "InvoiceNumber"),
                ("Invoices", "SupplierCompanyId"),
                ("Invoices", "CustomerCompanyId"),
                ("InvoiceItems", "Quantity"),
                ("InvoiceItems", "UnitPrice"),
                ("InvoiceItems", "VatRate"),
                ("MarketBiases", "Symbol"),
                ("MarketBiases", "BiasDate"),
                ("MarketBiases", "Direction"),
                ("MarketBiases", "SignalScore"),
                ("MarketBiases", "ConfidenceStars"),
                ("MarketBiases", "BulletJson"),
                ("StrategyNotes", "UserId"),
                ("StrategyNotes", "StrategyKey"),
                ("StrategyNotes", "ContentHtml"),
                ("StrategyNotes", "UpdatedAt"),
                ("TradeSetupReviews", "UserId"),
                ("TradeSetupReviews", "StrategyKey"),
                ("TradeSetupReviews", "TradeDate"),
                ("TradeSetupReviews", "Market"),
                ("TradeSetupReviews", "Verdict"),
                ("TradeSetupReviews", "SetupRating"),
                ("TradeSetupReviews", "ProcessScore"),
                ("TradeSetupReviews", "RMultiple"),
                ("TradeSetupReviews", "MissingInfo"),
                ("TradeSetupReviews", "Issues"),
                ("TradeSetupReviews", "NoTradeBlockers"),
                ("TradeSetupReviews", "AiReviewJson"),
                ("TradeSetupReviews", "ReviewedAt"),
                ("TradeSetupReviewImages", "TradeSetupReviewId"),
                ("TradeSetupReviewImages", "ImageData"),
                ("TradeSetupReviewImages", "ImageContentType"),
                ("TradeSetupReviewImages", "ImageFileName"),
            };

            foreach (var (table, column) in requiredColumns)
            {
                var exists = await ColumnExistsAsync(table, column, cancellationToken);
                checks.Add(new ReadinessCheck($"column:{table}.{column}", exists, exists ? "Column exists." : $"Missing column dbo.{table}.{column}."));
            }
        }

        private async Task AddJournalWriteSmokeCheckAsync(List<ReadinessCheck> checks, CancellationToken cancellationToken)
        {
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    var healthUserId = $"__health_check__{Guid.NewGuid():N}";

                    _context.Users.Add(new IdentityUser
                    {
                        Id = healthUserId,
                        UserName = $"{healthUserId}@edgedesk.local",
                        NormalizedUserName = $"{healthUserId}@EDGEDESK.LOCAL",
                        Email = $"{healthUserId}@edgedesk.local",
                        NormalizedEmail = $"{healthUserId}@EDGEDESK.LOCAL",
                        EmailConfirmed = true,
                        SecurityStamp = Guid.NewGuid().ToString("N"),
                        ConcurrencyStamp = Guid.NewGuid().ToString("N")
                    });

                    var entry = new JournalEntry
                    {
                        UserId = healthUserId,
                        Journal = new TradingJournalBook { UserId = healthUserId, Name = "Health check rollback journal" },
                        Asset = AssetSymbol.XAUUSD,
                        Side = PositionSide.Long,
                        CreatedAt = DateTimeOffset.UtcNow,
                        EntryTime = DateTimeOffset.UtcNow,
                        Status = EntryStatus.Open,
                        Outcome = TradeOutcome.Unknown,
                        RiskPercentage = 1m,
                        EntryPrice = 1m,
                        FollowedTradingPlan = true,
                        Note = "Health check rollback record",
                        Images =
                        {
                            new JournalEntryImage
                            {
                                ImageData = new byte[] { 1, 2, 3 },
                                ImageContentType = "image/png",
                                ImageFileName = "health-check.png",
                                CreatedAt = DateTimeOffset.UtcNow
                            }
                        }
                    };

                    _context.JournalEntries.Add(entry);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                });

                checks.Add(new ReadinessCheck("journal-write-smoke", true, "Journal insert with image succeeded and was rolled back."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness journal write smoke check failed.");
                _context.ChangeTracker.Clear();
                checks.Add(new ReadinessCheck("journal-write-smoke", false, ex.Message));
            }
        }

        private async Task AddSetupReviewWriteSmokeCheckAsync(List<ReadinessCheck> checks, CancellationToken cancellationToken)
        {
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    var review = new TradeSetupReview
                    {
                        UserId = $"__health_check__{Guid.NewGuid():N}",
                        StrategyKey = "corex",
                        TradeDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        TradeTimeText = "health",
                        Market = "MGC",
                        Direction = "LONG",
                        Session = "NY",
                        Status = "Rozpracováno",
                        Verdict = "Nelze rozhodnout",
                        MissingInfo = "Health check rollback record",
                        Notes = "Health check rollback record",
                        Images =
                        {
                            new TradeSetupReviewImage
                            {
                                ImageData = new byte[] { 1, 2, 3 },
                                ImageContentType = "image/png",
                                ImageFileName = "setup-health-check.png",
                                Label = "health",
                                CreatedAt = DateTimeOffset.UtcNow
                            }
                        }
                    };

                    _context.TradeSetupReviews.Add(review);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                });

                checks.Add(new ReadinessCheck("setup-review-write-smoke", true, "Setup review insert with image succeeded and was rolled back."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness setup review write smoke check failed.");
                _context.ChangeTracker.Clear();
                checks.Add(new ReadinessCheck("setup-review-write-smoke", false, ex.Message));
            }
        }

        private async Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken)
        {
            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                command.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table)";
            }
            else
            {
                command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'[dbo].[' + @table + ']', N'U') IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END";
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = table;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null && Convert.ToBoolean(result);
        }

        private async Task<bool> ColumnExistsAsync(string table, string column, CancellationToken cancellationToken)
        {
            var connection = _context.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                command.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table AND column_name = @column)";
            }
            else
            {
                command.CommandText = "SELECT CASE WHEN COL_LENGTH('[dbo].[' + @table + ']', @column) IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END";
            }

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = table;
            command.Parameters.Add(tableParameter);
            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "@column";
            columnParameter.Value = column;
            command.Parameters.Add(columnParameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null && Convert.ToBoolean(result);
        }

        private ObjectResult ReadinessResponse(IReadOnlyList<ReadinessCheck> checks, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            var healthy = checks.All(check => check.Healthy);

            return StatusCode(healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, new
            {
                status = healthy ? "Healthy" : "Unhealthy",
                checkedAt = DateTimeOffset.UtcNow,
                durationMs = stopwatch.ElapsedMilliseconds,
                checks
            });
        }

        private sealed record ReadinessCheck(string Name, bool Healthy, string Message);
    }
}
