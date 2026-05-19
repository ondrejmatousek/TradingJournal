using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnsureJournalOpenTradeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[dbo].[JournalEntries]', N'ExitPrice') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[JournalEntries] ALTER COLUMN [ExitPrice] decimal(18,8) NULL;
                END;

                IF COL_LENGTH(N'[dbo].[JournalEntries]', N'ExitTime') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[JournalEntries] ALTER COLUMN [ExitTime] datetimeoffset NULL;
                END;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
