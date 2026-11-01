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
using TradingJournal.Services.Valuation;
using TradingJournal.Services.Insiders;
using Npgsql;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("ScreeningWorker --check | --audit | --run [--force] [--screening-only] [--local]\n--audit writes coverage-audit.json from stored data without external API refresh. --screening-only refreshes the stock screener without macro context or insiders. SQL schema must already exist.");
    return 0;
}
if (args.Any(a => a is not ("--check" or "--run" or "--audit" or "--force" or "--local" or "--screening-only")) || args.Count(a => a is "--check" or "--run" or "--audit") != 1
    || args.Contains("--screening-only") && !args.Contains("--run"))
{
    Console.Error.WriteLine("Use --check or --run [--force].");
    return 1;
}
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
if (args.Contains("--local"))
{
    builder.Configuration.AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true);
    LocalDatabaseConfiguration.Apply(builder.Configuration);
}
var connection = PostgresConnectionString.Normalize(builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty);
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Missing ConnectionStrings__DefaultConnection. Use --local for the local SQL Server database.");
    return 1;
}
if (args.Contains("--run") && string.IsNullOrWhiteSpace(builder.Configuration["Screening:SecUserAgent"]))
{
    Console.Error.WriteLine("Missing Screening:SecUserAgent: configure a contact email for SEC in the web project's User Secrets. Local database configuration is present. --check can verify the database without this contact.");
    return 1;
}
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "[HH:mm:ss] "; });
builder.Logging.AddFilter("TradingJournal.Services", LogLevel.Information);
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
builder.Services.AddValuationV2(builder.Configuration);
builder.Services.AddScoped<ScreeningDataProvider>();
builder.Services.AddScoped<FmpFinancialProvider>();
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
    if (args.Contains("--audit"))
    {
        var audits = await scope.ServiceProvider.GetRequiredService<InvestmentScreeningService>().AuditAsync(cancellation.Token);
        var available = new[] { "price", "history", "financials", "valuation" }.ToDictionary(key => key,
            key => audits.Count(r => r.Checks.Any(c => c.Key == key && c.Status == "available")));
        var report = new { checkedAt = DateTimeOffset.UtcNow, total = audits.Count, ready = audits.Count(r => r.Ready), available,
            providerConfiguration = new { secContactConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Screening:SecUserAgent"]),
                fmpConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["FinancialModelingPrep:ApiKey"]) }, rows = audits };
        await File.WriteAllTextAsync("coverage-audit.json", System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }), cancellation.Token);
        Console.WriteLine($"Audit: {audits.Count} akcií, {report.ready} splňuje všechny kontroly. " + string.Join(", ", available.Select(x => $"{x.Key}: {x.Value}")) + ". Výsledek: coverage-audit.json");
        return 0;
    }
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
        await RunUpdateAsync(host, scope, cancellation.Token, args.Contains("--force"), args.Contains("--screening-only"));
    }
    else
    {
        await using var lease = new SqlConnection(new SqlConnectionStringBuilder(connection) { Pooling = false }.ConnectionString);
        await lease.OpenAsync(cancellation.Token);
        await using var acquire = lease.CreateCommand();
        acquire.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'EdgeDesk.ScreeningDaily', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync(cancellation.Token)) < 0)
        { Console.WriteLine("Another screening worker is running; no duplicate work started."); return 0; }
        await RunUpdateAsync(host, scope, cancellation.Token, args.Contains("--force"), args.Contains("--screening-only"));
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Update interrupted or exceeded the 50-minute limit. Saved data remain available; next run retries unfinished work."); return 3; }
catch (Exception ex)
{
    // Avoid logging connection strings or provider URLs with credentials.
    var detail = ex.Message.Replace(Environment.NewLine, " ").Replace("\r", " ").Replace("\n", " ");
    if (detail.Length > 800) detail = detail[..800];
    Console.Error.WriteLine($"Worker failed ({ex.GetType().Name}): {detail}");
    if (ex.InnerException is not null)
        Console.Error.WriteLine($"Inner error ({ex.InnerException.GetType().Name}): {ex.InnerException.Message.Replace(Environment.NewLine, " ")}");
    return 1;
}

static async Task RunUpdateAsync(IHost host, IServiceScope scope, CancellationToken cancellationToken, bool force, bool screeningOnly)
{
    if (screeningOnly)
    {
        await StageAsync("Akciový screener", () => host.Services.GetRequiredService<ScreeningUpdateHostedService>().RunIfDueAsync(DateTimeOffset.UtcNow, cancellationToken, force, refreshContext: false));
        return;
    }
    await StageAsync("1/2 Makro, geopolitika a akcie", () => host.Services.GetRequiredService<ScreeningUpdateHostedService>().RunIfDueAsync(DateTimeOffset.UtcNow, cancellationToken, force));
    var dueDate = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ScreeningSchedule>>().Value.DueDate(DateTimeOffset.UtcNow);
    TradingJournal.Models.InsiderImportRun insiderRun = null!;
    await StageAsync("2/2 Insiders", async () => { insiderRun = await scope.ServiceProvider.GetRequiredService<InsiderUpdateService>().RunAsync(dueDate, cancellationToken, force); });
    var run = await host.Services.GetRequiredService<ScreeningStore>().ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, cancellationToken);
    var summary = run == null ? "No completed update." : $"Stocks: {run.Processed}/{InvestmentUniverse.All.Count}; incomplete: {run.Incomplete}; macro: {run.MacroUpdated}. Insider filings: {insiderRun.FilingsImported}; transactions: {insiderRun.TransactionsImported}; issuer failures: {insiderRun.IssuersFailed}; completed: {insiderRun.CompletedAt:O}.";
    Console.WriteLine(summary);
    async Task StageAsync(string name, Func<Task> work)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {name}: začátek");
        using var stop = new CancellationTokenSource();
        var heartbeat = PulseAsync();
        try
        {
            await work();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {name}: krok ukončen za {watch.Elapsed:hh\\:mm\\:ss} (případná varování viz výše)");
        }
        finally
        {
            stop.Cancel();
            await heartbeat;
        }
        async Task PulseAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                while (await timer.WaitForNextTickAsync(stop.Token))
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {name}: stále běží nebo čeká na odpověď; uplynulo {watch.Elapsed:hh\\:mm\\:ss}. Poslední ticker viz výše.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
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




