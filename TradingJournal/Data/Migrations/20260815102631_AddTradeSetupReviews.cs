using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeSetupReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeSetupReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TradeTimeText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Market = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Session = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Verdict = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RMultiple = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: true),
                    Entry = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    StopLoss = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    TakeProfit = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PlannedRr = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    HtfContextScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    ZoneScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    LocalStructureScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    EntryConfirmationScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    TimingScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    RiskRewardScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    SessionScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    ManagementScore = table.Column<decimal>(type: "decimal(3,2)", precision: 3, scale: 2, nullable: true),
                    MissingInfo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Aligned = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Issues = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeSetupReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeSetupReviewImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeSetupReviewId = table.Column<int>(type: "int", nullable: false),
                    ImageData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ImageContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ImageFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                name: "TradeSetupReviewImages");

            migrationBuilder.DropTable(
                name: "TradeSetupReviews");
        }
    }
}
