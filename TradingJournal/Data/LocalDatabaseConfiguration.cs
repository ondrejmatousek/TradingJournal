using Microsoft.Extensions.Configuration;
namespace TradingJournal.Data;

public static class LocalDatabaseConfiguration
{
    public const string DefaultConnection = "Server=localhost\\SQLEXPRESS;Database=TradingJournal;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=true";
    public static void Apply(ConfigurationManager configuration)
    {
        var connection = configuration["LocalDatabase:ConnectionString"] ?? DefaultConnection;
        var sql = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
        if (sql.DataSource.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Local database mode must not target Azure SQL.");
        configuration.AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Turso:Enabled"] = "false", ["DataRefresh:Enabled"] = "false"
        });
    }
}
