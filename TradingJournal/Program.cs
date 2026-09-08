using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Services;

using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase)
        || connectionString.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null));
    else
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null));
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddHttpClient();
builder.Services.AddScoped<SeasonalityService>();
builder.Services.AddSingleton<SeasonalityUpdateState>();
builder.Services.AddHostedService<SeasonalityUpdateHostedService>();
builder.Services.AddScoped<CotDataService>();
builder.Services.AddHostedService<CotUpdateHostedService>();
builder.Services.AddSingleton<AppClock>();
builder.Services.AddSingleton<MuckaRubricProvider>();
builder.Services.AddScoped<MarketBiasService>();
builder.Services.AddScoped<StockRadarService>();
builder.Services.AddScoped<IUnderEma200HistoryProvider, UnderEma200HistoryProvider>();
builder.Services.AddScoped<UnderEma200Service>();
builder.Services.AddScoped<HomeInsightService>();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
        });
}

builder.Services.AddSingleton<TradingJournal.Services.Screening.ScreeningStore>();
builder.Services.AddScoped<TradingJournal.Services.Screening.ScreeningDataProvider>();
builder.Services.AddScoped<TradingJournal.Services.Screening.InvestmentScreeningService>();
builder.Services.AddScoped<TradingJournal.Services.Insiders.InsiderSecProvider>();
builder.Services.AddScoped<TradingJournal.Services.Insiders.IInsiderSecProvider>(sp => sp.GetRequiredService<TradingJournal.Services.Insiders.InsiderSecProvider>());
builder.Services.AddScoped<TradingJournal.Services.Insiders.InsiderUpdateService>();
builder.Services.AddOptions<TradingJournal.Services.Screening.ScreeningSchedule>()
    .Bind(builder.Configuration.GetSection("Screening:Schedule"))
    .PostConfigure(o =>
    {
        // Free App Service may sleep; the GitHub worker owns the cloud schedule.
        if (!string.IsNullOrEmpty(builder.Configuration["WEBSITE_SITE_NAME"]) || o.External)
        {
            o.External = true;
            o.Enabled = false;
        }
    })
    .Validate(o => o.Hour is >= 0 and <= 23 && o.Minute is >= 0 and <= 59, "Invalid screening schedule time.")
    .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Invalid screening time zone.")
    .ValidateOnStart();
builder.Services.AddHostedService<TradingJournal.Services.Screening.ScreeningUpdateHostedService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ✅ Safe migrations on startup (won't crash app)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        // jednoduchý retry (když je DB serverless/bootuje se)
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                db.Database.Migrate();
                SafeLog(() => logger.LogInformation("Database migration OK."));
                break;
            }
            catch (Exception ex) when (attempt < 5)
            {
                SafeLog(() => logger.LogWarning(ex, "Database migrate failed (attempt {Attempt}/5). Retrying...", attempt));
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
    catch (Exception ex)
    {
        SafeLog(() => logger.LogError(ex, "Database migration failed. App will continue running."));
        // app poběží dál → žádné 500.30
    }
}

static void SafeLog(Action writeLog)
{
    try
    {
        writeLog();
    }
    catch
    {
        // Some local Windows environments cannot write EventLog entries.
        // Logging must never prevent the web app from starting.
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.Run();
