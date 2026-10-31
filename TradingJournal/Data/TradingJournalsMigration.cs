using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace TradingJournal.Data;

public static class TradingJournalsMigration
{
    public static void Up(MigrationBuilder m)
    {
        m.CreateTable("TradingJournals", t => new
        {
            Id = t.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            UserId = t.Column<string>(maxLength: 450, nullable: false),
            Name = t.Column<string>(maxLength: 120, nullable: false),
            Description = t.Column<string>(maxLength: 1000, nullable: true),
            CreatedAt = t.Column<DateTimeOffset>(nullable: false)
        }, constraints: t => t.PrimaryKey("PK_TradingJournals", j => j.Id));
        m.CreateIndex("IX_TradingJournals_UserId_Name", "TradingJournals", new[] { "UserId", "Name" });
        m.AddColumn<int>("JournalId", "JournalEntries", nullable: true);
        if (m.ActiveProvider?.Contains("Npgsql", StringComparison.Ordinal) == true)
            m.Sql("""
                INSERT INTO "TradingJournals" ("UserId", "Name", "CreatedAt")
                SELECT DISTINCT "UserId", 'Můj trading journal', CURRENT_TIMESTAMP FROM "JournalEntries";
                UPDATE "JournalEntries" e SET "JournalId" = j."Id"
                FROM "TradingJournals" j WHERE e."UserId" = j."UserId";
                """);
        else
            m.Sql("""
                INSERT INTO [TradingJournals] ([UserId], [Name], [CreatedAt])
                SELECT DISTINCT [UserId], N'Můj trading journal', SYSDATETIMEOFFSET() FROM [JournalEntries];
                UPDATE e SET [JournalId] = j.[Id] FROM [JournalEntries] e
                INNER JOIN [TradingJournals] j ON e.[UserId] = j.[UserId];
                """);
        m.AlterColumn<int>("JournalId", "JournalEntries", nullable: false, oldClrType: typeof(int), oldNullable: true);
        m.CreateIndex("IX_JournalEntries_JournalId", "JournalEntries", "JournalId");
        m.CreateIndex("IX_JournalEntries_UserId_JournalId", "JournalEntries", new[] { "UserId", "JournalId" });
        m.AddForeignKey("FK_JournalEntries_TradingJournals_JournalId", "JournalEntries", "JournalId", "TradingJournals", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    public static void Down(MigrationBuilder m)
    {
        // Rolling back the feature leaves all trades and their images intact.
        m.DropForeignKey("FK_JournalEntries_TradingJournals_JournalId", "JournalEntries");
        m.DropIndex("IX_JournalEntries_JournalId", "JournalEntries");
        m.DropIndex("IX_JournalEntries_UserId_JournalId", "JournalEntries");
        m.DropColumn("JournalId", "JournalEntries");
        m.DropTable("TradingJournals");
    }
}
