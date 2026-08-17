using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCotData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CotImportRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    RowsImported = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CotReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SymbolKey = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CftcMarketName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    CftcContractMarketCode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    SourceDataset = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    TraderCategory = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LongPosition = table.Column<long>(type: "bigint", nullable: false),
                    ShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    NetPosition = table.Column<long>(type: "bigint", nullable: false),
                    WeeklyNetChange = table.Column<long>(type: "bigint", nullable: true),
                    LongChange = table.Column<long>(type: "bigint", nullable: true),
                    ShortChange = table.Column<long>(type: "bigint", nullable: true),
                    OpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    CotIndex26 = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: true),
                    CotIndex52 = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: true),
                    Percentile52 = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: true),
                    ZScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ExtremeLong = table.Column<bool>(type: "bit", nullable: false),
                    ExtremeShort = table.Column<bool>(type: "bit", nullable: false),
                    Bias = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CotReports_SymbolKey_ReportDate",
                table: "CotReports",
                columns: new[] { "SymbolKey", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CotReports_SymbolKey_ReportDate_SourceDataset_TraderCategory",
                table: "CotReports",
                columns: new[] { "SymbolKey", "ReportDate", "SourceDataset", "TraderCategory" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotImportRuns");

            migrationBuilder.DropTable(
                name: "CotReports");
        }
    }
}
