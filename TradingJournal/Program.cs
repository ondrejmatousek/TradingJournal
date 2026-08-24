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
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));

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
