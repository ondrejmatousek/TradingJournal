using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingJournal.Services;

namespace TradingJournal.Tests;

public class TursoRecentPricesTests
{
    [Fact]
    public async Task RecentReadIsBoundedInSqlWhileTradingCanStillReadFullHistory()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var today = new DateOnly(2026, 9, 9);
        using (var seed = connection.CreateCommand())
        {
            seed.CommandText = "CREATE TABLE SeasonalityPrices (AssetKey TEXT, Date TEXT, Close REAL, PRIMARY KEY(AssetKey,Date));";
            await seed.ExecuteNonQueryAsync();
            seed.CommandText = "INSERT INTO SeasonalityPrices VALUES ($key,$date,100)";
            seed.Parameters.AddWithValue("$key", "MSFT");
            seed.Parameters.AddWithValue("$date", "");
            foreach (var key in new[] { "MSFT", "OTHER" })
                for (var day = -600; day <= 1; day++)
                {
                    seed.Parameters["$key"].Value = key;
                    seed.Parameters["$date"].Value = today.AddDays(day).ToString("yyyy-MM-dd");
                    await seed.ExecuteNonQueryAsync();
                }
        }
        using var transport = new SqlTransport(connection);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Turso:DatabaseUrl"] = "https://test.invalid", ["Turso:AuthToken"] = "test" }).Build();
        var store = new TursoSeasonalityStore(config, transport, NullLogger<TursoSeasonalityStore>.Instance);
        var recent = await store.LoadRecentAsync("MSFT", today);
        Assert.Equal(252, transport.ReturnedRows);
        Assert.Equal(252, recent.Count);
        Assert.Equal(today.AddDays(-251), recent[0].Date);
        Assert.Equal(today, recent[^1].Date);
        Assert.Empty(await store.LoadRecentAsync("MISSING", today));
        Assert.Equal(602, (await store.LoadAsync("MSFT")).Count);
    }

    // Executes the actual outgoing Turso SQL against SQLite; no live database/network.
    private sealed class SqlTransport(SqliteConnection connection) : HttpMessageHandler, IHttpClientFactory
    {
        public int ReturnedRows { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var stmt = body.RootElement.GetProperty("requests")[0].GetProperty("stmt");
            using var command = connection.CreateCommand();
            var sql = stmt.GetProperty("sql").GetString()!;
            var index = 0;
            foreach (var arg in stmt.GetProperty("args").EnumerateArray())
            {
                var name = "$p" + index++;
                var position = sql.IndexOf('?');
                sql = sql[..position] + name + sql[(position + 1)..];
                command.Parameters.AddWithValue(name, arg.GetProperty("value").GetString());
            }
            command.CommandText = sql;
            using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<object[]>();
            while (await reader.ReadAsync(ct)) rows.Add([new { type = "text", value = reader.GetString(0) }, new { type = "float", value = reader.GetDouble(1) }]);
            ReturnedRows = rows.Count;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { results = new[] { new { type = "ok", response = new { result = new { rows } } } } })) };
        }
    }
}
