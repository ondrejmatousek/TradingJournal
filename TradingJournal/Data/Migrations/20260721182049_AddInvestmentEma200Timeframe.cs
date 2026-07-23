using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentEma200Timeframe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.AddColumn<string>(
                name: "Timeframe",
                table: "InvestmentEma200Snapshots",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol_Timeframe",
                table: "InvestmentEma200Snapshots",
                columns: new[] { "Symbol", "Timeframe" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol_Timeframe",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "Timeframe",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol",
                table: "InvestmentEma200Snapshots",
                column: "Symbol",
                unique: true);
        }
    }
}
