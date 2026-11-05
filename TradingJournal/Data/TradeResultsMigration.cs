using Microsoft.EntityFrameworkCore.Migrations;

namespace TradingJournal.Data;

public static class TradeResultsMigration
{
    public static void Up(MigrationBuilder m)
    {
        m.AddColumn<decimal>("ProfitLoss", "JournalEntries", precision: 18, scale: 2, nullable: true);
        m.AddColumn<int>("PlannedOutcome", "JournalEntries", nullable: true);
        m.AddColumn<bool>("SeasonalityAligned", "JournalEntries", nullable: true);
    }

    public static void Down(MigrationBuilder m)
    {
        m.DropColumn("ProfitLoss", "JournalEntries");
        m.DropColumn("PlannedOutcome", "JournalEntries");
        m.DropColumn("SeasonalityAligned", "JournalEntries");
    }
}
