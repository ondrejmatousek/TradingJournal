using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();

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
                logger.LogInformation("Database migration OK.");
                break;
            }
            catch (Exception ex) when (attempt < 5)
            {
                logger.LogWarning(ex, "Database migrate failed (attempt {Attempt}/5). Retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed. App will continue running.");
        // app poběží dál → žádné 500.30
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

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=JournalEntries}/{action=Index}/{id?}");

app.MapRazorPages();
app.Run();