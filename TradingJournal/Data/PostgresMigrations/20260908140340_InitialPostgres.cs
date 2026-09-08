using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingJournal.Data.PostgresMigrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CotImportRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    RowsImported = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CotReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SymbolKey = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CftcMarketName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    CftcContractMarketCode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    SourceDataset = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    TraderCategory = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LongPosition = table.Column<long>(type: "bigint", nullable: false),
                    ShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    NetPosition = table.Column<long>(type: "bigint", nullable: false),
                    WeeklyNetChange = table.Column<long>(type: "bigint", nullable: true),
                    LongChange = table.Column<long>(type: "bigint", nullable: true),
                    ShortChange = table.Column<long>(type: "bigint", nullable: true),
                    OpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    CotIndex26 = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: true),
                    CotIndex52 = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: true),
                    Percentile52 = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: true),
                    ZScore = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    ExtremeLong = table.Column<bool>(type: "boolean", nullable: false),
                    ExtremeShort = table.Column<bool>(type: "boolean", nullable: false),
                    Bias = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsiderFilings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccessionNumber = table.Column<string>(type: "character varying(24)", unicode: false, maxLength: 24, nullable: false),
                    IssuerCik = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    FormType = table.Column<string>(type: "character varying(8)", unicode: false, maxLength: 8, nullable: false),
                    PeriodOfReport = table.Column<DateOnly>(type: "date", nullable: true),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", unicode: false, maxLength: 1000, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderFilings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsiderImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IssuersChecked = table.Column<int>(type: "integer", nullable: false),
                    FilingsImported = table.Column<int>(type: "integer", nullable: false),
                    TransactionsImported = table.Column<int>(type: "integer", nullable: false),
                    IssuersFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentEma200Snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Sector = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Ema200Daily = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Ema200Weekly = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Performance1YearPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Performance3YearPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Performance5YearPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    DailyPriceCount = table.Column<int>(type: "integer", nullable: false),
                    DailyEmaSourceCount = table.Column<int>(type: "integer", nullable: false),
                    WeeklyEmaSourceCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastPriceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CalculationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataSource = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEma200Snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TaxNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsVatPayer = table.Column<bool>(type: "boolean", nullable: false),
                    Street = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: true),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ZipCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Phone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BankAccount = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Iban = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsDefaultSupplier = table.Column<bool>(type: "boolean", nullable: false),
                    LastHourlyRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCompanies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Asset = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntryTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExitTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FeelingsBeforeEntry = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    RiskPercentage = table.Column<decimal>(type: "numeric(9,5)", precision: 9, scale: 5, nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ExitPrice = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    FollowedTradingPlan = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ImageData = table.Column<byte[]>(type: "bytea", nullable: true),
                    ImageContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ImageFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketBiases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BiasDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SignalScore = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceStars = table.Column<int>(type: "integer", nullable: false),
                    LastClose = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    ChangePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Rsi14 = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Ma20 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    Ma50 = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    Summary = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: false),
                    BulletJson = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketBiases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningDocuments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonalityPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Close = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonalityPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StrategyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeSetupReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StrategyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TradeTimeText = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Market = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Session = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Verdict = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SetupRating = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ProcessScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RMultiple = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    IsValid = table.Column<bool>(type: "boolean", nullable: true),
                    Entry = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    StopLoss = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    TakeProfit = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    PlannedRr = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    HtfContextScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    ZoneScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    LocalStructureScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    EntryConfirmationScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    TimingScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    RiskRewardScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    SessionScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    ManagementScore = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    MissingInfo = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    Aligned = table.Column<string>(type: "text", nullable: false),
                    Issues = table.Column<string>(type: "text", nullable: false),
                    NoTradeBlockers = table.Column<string>(type: "text", nullable: false),
                    AiReviewJson = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeSetupReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsiderReportingOwners",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InsiderFilingId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerCik = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: true),
                    Name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    IsDirector = table.Column<bool>(type: "boolean", nullable: false),
                    IsOfficer = table.Column<bool>(type: "boolean", nullable: false),
                    IsTenPercentOwner = table.Column<bool>(type: "boolean", nullable: false),
                    IsOther = table.Column<bool>(type: "boolean", nullable: false),
                    OfficerTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderReportingOwners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderReportingOwners_InsiderFilings_InsiderFilingId",
                        column: x => x.InsiderFilingId,
                        principalTable: "InsiderFilings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsiderTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InsiderFilingId = table.Column<long>(type: "bigint", nullable: false),
                    SourceIndex = table.Column<int>(type: "integer", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SecurityTitle = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    TransactionCode = table.Column<string>(type: "character varying(4)", unicode: false, maxLength: 4, nullable: false),
                    AcquisitionDisposition = table.Column<string>(type: "character varying(1)", unicode: false, maxLength: 1, nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false),
                    PricePerShare = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: true),
                    SharesOwnedAfter = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: true),
                    DirectIndirect = table.Column<string>(type: "character varying(1)", unicode: false, maxLength: 1, nullable: true),
                    IsDerivative = table.Column<bool>(type: "boolean", nullable: false),
                    IsTenB5One = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderTransactions_InsiderFilings_InsiderFilingId",
                        column: x => x.InsiderFilingId,
                        principalTable: "InsiderFilings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SupplierCompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerCompanyId = table.Column<int>(type: "integer", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TaxableSupplyDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_InvoiceCompanies_CustomerCompanyId",
                        column: x => x.CustomerCompanyId,
                        principalTable: "InvoiceCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_InvoiceCompanies_SupplierCompanyId",
                        column: x => x.SupplierCompanyId,
                        principalTable: "InvoiceCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: false),
                    ImageData = table.Column<byte[]>(type: "bytea", nullable: false),
                    ImageContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ImageFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryImages_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeSetupReviewImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TradeSetupReviewId = table.Column<int>(type: "integer", nullable: false),
                    ImageData = table.Column<byte[]>(type: "bytea", nullable: false),
                    ImageContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ImageFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeSetupReviewImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeSetupReviewImages_TradeSetupReviews_TradeSetupReviewId",
                        column: x => x.TradeSetupReviewId,
                        principalTable: "TradeSetupReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CotReports_SymbolKey_ReportDate",
                table: "CotReports",
                columns: new[] { "SymbolKey", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CotReports_SymbolKey_ReportDate_SourceDataset_TraderCategory",
                table: "CotReports",
                columns: new[] { "SymbolKey", "ReportDate", "SourceDataset", "TraderCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderFilings_AccessionNumber",
                table: "InsiderFilings",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderFilings_Symbol_FiledDate",
                table: "InsiderFilings",
                columns: new[] { "Symbol", "FiledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderImportRuns_ScheduledDate",
                table: "InsiderImportRuns",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderReportingOwners_InsiderFilingId",
                table: "InsiderReportingOwners",
                column: "InsiderFilingId");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransactions_InsiderFilingId_SourceIndex",
                table: "InsiderTransactions",
                columns: new[] { "InsiderFilingId", "SourceIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransactions_TransactionDate",
                table: "InsiderTransactions",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol",
                table: "InvestmentEma200Snapshots",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCompanies_UserId_Name",
                table: "InvoiceCompanies",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InvoiceId",
                table: "InvoiceItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerCompanyId",
                table: "Invoices",
                column: "CustomerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SupplierCompanyId",
                table: "Invoices",
                column: "SupplierCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "UserId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_IssueDate",
                table: "Invoices",
                columns: new[] { "UserId", "IssueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_UserId_CreatedAt",
                table: "JournalEntries",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryImages_JournalEntryId",
                table: "JournalEntryImages",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketBiases_Symbol_BiasDate",
                table: "MarketBiases",
                columns: new[] { "Symbol", "BiasDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonalityPrices_AssetKey_Date",
                table: "SeasonalityPrices",
                columns: new[] { "AssetKey", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyNotes_UserId_StrategyKey",
                table: "StrategyNotes",
                columns: new[] { "UserId", "StrategyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeSetupReviewImages_TradeSetupReviewId",
                table: "TradeSetupReviewImages",
                column: "TradeSetupReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeSetupReviews_UserId_TradeDate",
                table: "TradeSetupReviews",
                columns: new[] { "UserId", "TradeDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CotImportRuns");

            migrationBuilder.DropTable(
                name: "CotReports");

            migrationBuilder.DropTable(
                name: "InsiderImportRuns");

            migrationBuilder.DropTable(
                name: "InsiderReportingOwners");

            migrationBuilder.DropTable(
                name: "InsiderTransactions");

            migrationBuilder.DropTable(
                name: "InvestmentEma200Snapshots");

            migrationBuilder.DropTable(
                name: "InvoiceItems");

            migrationBuilder.DropTable(
                name: "JournalEntryImages");

            migrationBuilder.DropTable(
                name: "MarketBiases");

            migrationBuilder.DropTable(
                name: "ScreeningDocuments");

            migrationBuilder.DropTable(
                name: "SeasonalityPrices");

            migrationBuilder.DropTable(
                name: "StrategyNotes");

            migrationBuilder.DropTable(
                name: "TradeSetupReviewImages");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "InsiderFilings");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "TradeSetupReviews");

            migrationBuilder.DropTable(
                name: "InvoiceCompanies");
        }
    }
}
