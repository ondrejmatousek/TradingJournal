using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderFilings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsiderFilings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessionNumber = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    IssuerCik = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Symbol = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    IssuerName = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    FormType = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    PeriodOfReport = table.Column<DateOnly>(type: "date", nullable: true),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceUrl = table.Column<string>(type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsAmendment = table.Column<bool>(type: "bit", nullable: false)
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
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IssuersChecked = table.Column<int>(type: "int", nullable: false),
                    FilingsImported = table.Column<int>(type: "int", nullable: false),
                    TransactionsImported = table.Column<int>(type: "int", nullable: false),
                    IssuersFailed = table.Column<int>(type: "int", nullable: false),
                    ErrorSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsiderReportingOwners",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsiderFilingId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerCik = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    IsDirector = table.Column<bool>(type: "bit", nullable: false),
                    IsOfficer = table.Column<bool>(type: "bit", nullable: false),
                    IsTenPercentOwner = table.Column<bool>(type: "bit", nullable: false),
                    IsOther = table.Column<bool>(type: "bit", nullable: false),
                    OfficerTitle = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
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
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsiderFilingId = table.Column<long>(type: "bigint", nullable: false),
                    SourceIndex = table.Column<int>(type: "int", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SecurityTitle = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    TransactionCode = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
                    AcquisitionDisposition = table.Column<string>(type: "varchar(1)", unicode: false, maxLength: 1, nullable: false),
                    Shares = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    PricePerShare = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: true),
                    SharesOwnedAfter = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: true),
                    DirectIndirect = table.Column<string>(type: "varchar(1)", unicode: false, maxLength: 1, nullable: true),
                    IsDerivative = table.Column<bool>(type: "bit", nullable: false),
                    IsTenB5One = table.Column<bool>(type: "bit", nullable: true)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsiderImportRuns");

            migrationBuilder.DropTable(
                name: "InsiderReportingOwners");

            migrationBuilder.DropTable(
                name: "InsiderTransactions");

            migrationBuilder.DropTable(
                name: "InsiderFilings");
        }
    }
}
