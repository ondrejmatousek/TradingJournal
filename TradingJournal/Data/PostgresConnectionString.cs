namespace TradingJournal.Data;

public static class PostgresConnectionString
{
    public static string Normalize(string connectionString)
    {
        if (!connectionString.TrimStart().StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.TrimStart().StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2 || string.IsNullOrWhiteSpace(uri.Host))
            throw new FormatException("Neon PostgreSQL URI musí obsahovat uživatele a heslo.");

        var database = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
            throw new FormatException("Neon PostgreSQL URI musí obsahovat název databáze.");

        return string.Join(';',
            $"Host={uri.Host}",
            $"Port={uri.Port switch { -1 => 5432, var port => port }}",
            $"Initial Catalog={Uri.UnescapeDataString(database)}",
            $"Username={Uri.UnescapeDataString(userInfo[0])}",
            $"Password={Uri.UnescapeDataString(userInfo[1])}",
            "SSL Mode=Require",
            "Channel Binding=Require");
    }
}
