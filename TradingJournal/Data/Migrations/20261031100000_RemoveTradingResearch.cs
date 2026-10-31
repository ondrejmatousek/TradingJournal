using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace TradingJournal.Data.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261031100000_RemoveTradingResearch")]
public sealed class RemoveTradingResearch : Migration
{
    protected override void Up(MigrationBuilder m) => RemoveTradingResearchMigration.Up(m);
    protected override void Down(MigrationBuilder m) => RemoveTradingResearchMigration.Down(m);
}
