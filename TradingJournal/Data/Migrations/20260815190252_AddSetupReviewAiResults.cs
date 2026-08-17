using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupReviewAiResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiReviewJson",
                table: "TradeSetupReviews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoTradeBlockers",
                table: "TradeSetupReviews",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ProcessScore",
                table: "TradeSetupReviews",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "TradeSetupReviews",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupRating",
                table: "TradeSetupReviews",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiReviewJson",
                table: "TradeSetupReviews");

            migrationBuilder.DropColumn(
                name: "NoTradeBlockers",
                table: "TradeSetupReviews");

            migrationBuilder.DropColumn(
                name: "ProcessScore",
                table: "TradeSetupReviews");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "TradeSetupReviews");

            migrationBuilder.DropColumn(
                name: "SetupRating",
                table: "TradeSetupReviews");
        }
    }
}
