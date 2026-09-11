using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradingJournal.Data;

/// <summary>
/// Keeps PostgreSQL migrations deterministic and independent of a developer's local
/// SQL Server connection string. Runtime configuration still selects the provider.
/// </summary>
public sealed class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection = PostgresConnectionString.Normalize(
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=TradingJournal;Username=postgres;Password=design-time");
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        var options = connection.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ? optionsBuilder
            .UseNpgsql(connection, npgsql => npgsql.EnableRetryOnFailure())
            .Options : optionsBuilder.UseSqlServer(connection).Options;
        return new ApplicationDbContext(options);
    }
}

