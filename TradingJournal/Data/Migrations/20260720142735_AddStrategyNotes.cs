using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StrategyNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyNotes_UserId_StrategyKey",
                table: "StrategyNotes",
                columns: new[] { "UserId", "StrategyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyNotes");
        }
    }
}
