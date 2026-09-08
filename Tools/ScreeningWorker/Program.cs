using System.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingJournal.Data;
using TradingJournal.Services;
using TradingJournal.Services.Screening;
using TradingJournal.Services.Insiders;
using Npgsql;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("ScreeningWorker --check | --run [--force]\nRequires ConnectionStrings__DefaultConnection and Screening__SecUserAgent. SQL schema must already be deployed by the web app. No web server or migrations are started.");
    return 0;
}
if (args.Any(a => a is not ("--check" or "--run" or "--force")) || args.Contains("--check") == args.Contains("--run"))
{
    Console.Error.WriteLine("Use --check or --run [--force].");
    return 1;
}
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
var connection = PostgresConnectionString.Normalize(builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty);
if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(builder.Configuration["Screening:SecUserAgent"]))
{
    Console.Error.WriteLine("Missing ConnectionStrings__DefaultConnection or Screening__SecUserAgent. Configure secrets; do not pass credentials as command arguments.");
    return 1;
}
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddSingleton<IWebHostEnvironment>(new WorkerEnvironment());
builder.Services.AddDbContext<ApplicationDbContext>(o =>
{
    if (connection.StartsWith("Host=", StringComparison.OrdinalIgnoreCase)
        || connection.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        o.UseNpgsql(connection, npgsql => npgsql.EnableRetryOnFailure(3));
    else
        o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3));
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ScreeningStore>();
builder.Services.AddScoped<ScreeningDataProvider>();
builder.Services.AddScoped<IUnderEma200HistoryProvider, UnderEma200HistoryProvider>();
builder.Services.AddScoped<InvestmentScreeningService>();
builder.Services.AddScoped<InsiderSecProvider>();
builder.Services.AddScoped<IInsiderSecProvider>(sp => sp.GetRequiredService<InsiderSecProvider>());
builder.Services.AddScoped<InsiderUpdateService>();
builder.Services.AddOptions<ScreeningSchedule>().Configure(o => { o.Enabled = true; o.Hour = 6; o.Minute = 17; });
builder.Services.AddSingleton<ScreeningUpdateHostedService>();
using var host = builder.Build();
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(50));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Check actual table access, not just TCP connectivity. Do not migrate with the worker's limited identity.
    await db.ScreeningDocuments.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).Take(1).ToListAsync(cancellation.Token);
    await db.InsiderFilings.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).Take(1).ToListAsync(cancellation.Token);
    if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Docs/investments/sec-tickers.json")))
        throw new InvalidOperationException("Missing bundled SEC ticker mapping.");
    if (args.Contains("--check")) { Console.WriteLine("SQL screening storage and bundled provider mapping are ready."); return 0; }

    // Session lock prevents overlapping runs. PostgreSQL uses an advisory lock;
    // the legacy Azure SQL path keeps sp_getapplock.
    if (connection.StartsWith("Host=", StringComparison.OrdinalIgnoreCase)
        || connection.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
    {
        await using var lease = new NpgsqlConnection(connection);
        await lease.OpenAsync(cancellation.Token);
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended('EdgeDesk.ScreeningDaily', 0));", lease);
        if (!Convert.ToBoolean(await acquire.ExecuteScalarAsync(cancellation.Token)))
        { Console.WriteLine("Another screening worker is running; no duplicate work started."); return 0; }
        await RunUpdateAsync(host, scope, cancellation.Token, args.Contains("--force"));
    }
    else
    {
        await using var lease = new SqlConnection(new SqlConnectionStringBuilder(connection) { Pooling = false }.ConnectionString);
        await lease.OpenAsync(cancellation.Token);
        await using var acquire = lease.CreateCommand();
        acquire.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'EdgeDesk.ScreeningDaily', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync(cancellation.Token)) < 0)
        { Console.WriteLine("Another screening worker is running; no duplicate work started."); return 0; }
        await RunUpdateAsync(host, scope, cancellation.Token, args.Contains("--force"));
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Update interrupted or exceeded the 50-minute limit. Saved data remain available; next run retries unfinished work."); return 3; }
catch (Exception ex)
{
    // Avoid logging connection strings or provider URLs with credentials.
    Console.Error.WriteLine($"Worker failed ({ex.GetType().Name}). Check SQL permissions, schema, firewall and configured secrets.");
    return 1;
}

static async Task RunUpdateAsync(IHost host, IServiceScope scope, CancellationToken cancellationToken, bool force)
{
    await host.Services.GetRequiredService<ScreeningUpdateHostedService>().RunIfDueAsync(DateTimeOffset.UtcNow, cancellationToken, force);
    var dueDate = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ScreeningSchedule>>().Value.DueDate(DateTimeOffset.UtcNow);
    var insiderRun = await scope.ServiceProvider.GetRequiredService<InsiderUpdateService>().RunAsync(dueDate, cancellationToken, force);
    var run = await host.Services.GetRequiredService<ScreeningStore>().ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, cancellationToken);
    var summary = run == null ? "No completed update." : $"Stocks: {run.Processed}/{InvestmentUniverse.All.Count}; incomplete: {run.Incomplete}; macro: {run.MacroUpdated}. Insider filings: {insiderRun.FilingsImported}; transactions: {insiderRun.TransactionsImported}; issuer failures: {insiderRun.IssuersFailed}; completed: {insiderRun.CompletedAt:O}.";
    Console.WriteLine(summary);
    var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    if (!string.IsNullOrEmpty(summaryPath)) await File.AppendAllTextAsync(summaryPath, summary + Environment.NewLine, cancellationToken);
}

sealed class WorkerEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ScreeningWorker";
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
