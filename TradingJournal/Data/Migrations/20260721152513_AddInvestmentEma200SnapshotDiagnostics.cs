using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentEma200SnapshotDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "InvestmentEma200Snapshots",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBelowEma200",
                table: "InvestmentEma200Snapshots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "InvestmentEma200Snapshots",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnapshotCreatedAtUtc",
                table: "InvestmentEma200Snapshots",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "IsBelowEma200",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "SnapshotCreatedAtUtc",
                table: "InvestmentEma200Snapshots");
        }
    }
}
