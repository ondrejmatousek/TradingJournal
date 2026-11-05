using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using TradingJournal.Data;
namespace TradingJournal.Tests;
public class LocalDatabaseTests
{
    [Fact] public void LocalModeOverridesCloudConnectionAndDisablesBackgroundRefresh()
    {
        var c = new ConfigurationManager(); c["ConnectionStrings:DefaultConnection"]="Host=cloud"; c["DataRefresh:Enabled"]="true";
        LocalDatabaseConfiguration.Apply(c);
        Assert.Equal(LocalDatabaseConfiguration.DefaultConnection,c.GetConnectionString("DefaultConnection"));
        Assert.False(c.GetValue<bool>("DataRefresh:Enabled"));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void MigrationHistoriesStaySeparated(bool sql)
    {
        var b=new DbContextOptionsBuilder<ApplicationDbContext>();
        if(sql) b.UseSqlServer(LocalDatabaseConfiguration.DefaultConnection); else b.UseNpgsql("Host=localhost;Database=test");
        using var db=new ApplicationDbContext(b.Options);
        var migrations=db.GetService<IMigrationsAssembly>().Migrations;
        Assert.Contains("20260910120000_ValuationV2",migrations.Keys);
        Assert.Equal(sql,migrations.ContainsKey("20260908070757_AddInsiderFilings"));
        Assert.Equal(!sql,migrations.ContainsKey("20260908140340_InitialPostgres"));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void TradeResultsMigrationGeneratesAdditiveSqlForBothProviders(bool sql)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (sql) options.UseSqlServer(LocalDatabaseConfiguration.DefaultConnection);
        else options.UseNpgsql("Host=localhost;Database=test");
        using var db = new ApplicationDbContext(options.Options);
        var script = db.GetService<IMigrator>().GenerateScript("20261031100000_RemoveTradingResearch", "20261105090000_AddTradeResults");
        foreach (var name in new[] { "ProfitLoss", "PlannedOutcome", "SeasonalityAligned" })
            Assert.Contains(name, script);
        Assert.Contains(sql ? "decimal(18,2)" : "numeric(18,2)", script);
        Assert.DoesNotContain("DROP", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExitTime", script);
    }
}
