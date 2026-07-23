using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeInvestmentEma200Snapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol_Timeframe",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.AddColumn<int>(
                name: "DailyEmaSourceCount",
                table: "InvestmentEma200Snapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DailyPriceCount",
                table: "InvestmentEma200Snapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Ema200Daily",
                table: "InvestmentEma200Snapshots",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Ema200Weekly",
                table: "InvestmentEma200Snapshots",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyEmaSourceCount",
                table: "InvestmentEma200Snapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE target
                SET Ema200Daily = source.Ema200
                FROM InvestmentEma200Snapshots AS target
                INNER JOIN InvestmentEma200Snapshots AS source
                    ON source.Symbol = target.Symbol
                   AND source.Timeframe = 'Daily';

                UPDATE target
                SET Ema200Weekly = source.Ema200
                FROM InvestmentEma200Snapshots AS target
                INNER JOIN InvestmentEma200Snapshots AS source
                    ON source.Symbol = target.Symbol
                   AND source.Timeframe = 'Weekly';

                UPDATE InvestmentEma200Snapshots
                SET Ema200Daily = CASE
                        WHEN Ema200Daily = 0 AND Timeframe = 'Daily' THEN Ema200
                        ELSE Ema200Daily
                    END,
                    Ema200Weekly = CASE
                        WHEN Ema200Weekly = 0 AND Timeframe = 'Weekly' THEN Ema200
                        ELSE Ema200Weekly
                    END;

                UPDATE InvestmentEma200Snapshots
                SET DailyEmaSourceCount = CASE WHEN Ema200Daily > 0 THEN 200 ELSE 0 END,
                    WeeklyEmaSourceCount = CASE WHEN Ema200Weekly > 0 THEN 200 ELSE 0 END,
                    DailyPriceCount = CASE WHEN CurrentPrice > 0 THEN 1260 ELSE 0 END;

                WITH RankedSnapshots AS
                (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY Symbol
                            ORDER BY
                                CASE WHEN Ema200Daily > 0 AND Ema200Weekly > 0 THEN 0 ELSE 1 END,
                                UpdatedAt DESC,
                                Id DESC
                        ) AS RowNumber
                    FROM InvestmentEma200Snapshots
                )
                DELETE FROM InvestmentEma200Snapshots
                WHERE Id IN
                (
                    SELECT Id
                    FROM RankedSnapshots
                    WHERE RowNumber > 1
                );
                """);

            migrationBuilder.DropColumn(
                name: "DistanceFromEmaPercent",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "Ema200",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "FailureReasons",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "IsBelowEma200",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "IsQualified",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "Score",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "DailyEmaSourceCount",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "DailyPriceCount",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "Ema200Daily",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.AddColumn<decimal>(
                name: "DistanceFromEmaPercent",
                table: "InvestmentEma200Snapshots",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Ema200",
                table: "InvestmentEma200Snapshots",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FailureReasons",
                table: "InvestmentEma200Snapshots",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBelowEma200",
                table: "InvestmentEma200Snapshots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsQualified",
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

            migrationBuilder.AddColumn<string>(
                name: "Timeframe",
                table: "InvestmentEma200Snapshots",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "InvestmentEma200Snapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE InvestmentEma200Snapshots
                SET Timeframe = 'Daily',
                    Ema200 = Ema200Daily;
                """);

            migrationBuilder.DropColumn(
                name: "Ema200Weekly",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.DropColumn(
                name: "WeeklyEmaSourceCount",
                table: "InvestmentEma200Snapshots");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEma200Snapshots_Symbol_Timeframe",
                table: "InvestmentEma200Snapshots",
                columns: new[] { "Symbol", "Timeframe" },
                unique: true);
        }
    }
}
