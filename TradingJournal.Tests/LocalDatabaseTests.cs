using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using TradingJournal.Data;
namespace TradingJournal.Tests;
public class LocalDatabaseTests
{
    [Fact] public void LocalModeOverridesCloudConnectionAndDisablesTurso()
    {
        var c = new ConfigurationManager(); c["ConnectionStrings:DefaultConnection"]="Host=cloud"; c["Turso:Enabled"]="true";
        LocalDatabaseConfiguration.Apply(c);
        Assert.Equal(LocalDatabaseConfiguration.DefaultConnection,c.GetConnectionString("DefaultConnection"));
        Assert.False(c.GetValue<bool>("Turso:Enabled"));
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
}
