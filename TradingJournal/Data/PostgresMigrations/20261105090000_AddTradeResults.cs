using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TradingJournal.Data.PostgresMigrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261105090000_AddTradeResults")]
public sealed class AddTradeResults : Migration
{
    protected override void Up(MigrationBuilder m) => TradeResultsMigration.Up(m);
    protected override void Down(MigrationBuilder m) => TradeResultsMigration.Down(m);
}
