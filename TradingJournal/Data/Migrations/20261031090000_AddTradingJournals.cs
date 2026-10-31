using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TradingJournal.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261031090000_AddTradingJournals")]
public sealed class AddTradingJournals : Migration
{
    protected override void Up(MigrationBuilder m) => TradingJournalsMigration.Up(m);
    protected override void Down(MigrationBuilder m) => TradingJournalsMigration.Down(m);
}
