using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingJournal.Data;
using TradingJournal.Models;

namespace TradingJournal.Tests;

public class MultipleJournalsTests
{
    [Fact]
    public async Task RetiredResearchModulesHaveNoRoutesAssetsNavigationOrDatabaseModels()
    {
        await using var app = new JournalApp();
        using var client = await app.ClientAsync("owner");
        foreach (var path in new[] { "/Cot", "/Cot/Update", "/Seasonality", "/Seasonality/Data", "/js/cot-chart.js", "/js/cot-page-refresh.js", "/js/seasonality.js", "/js/seasonality-geometry.js" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        var html = await client.GetStringAsync("/TradingJournals");
        Assert.DoesNotContain("COT Dashboard", html);
        Assert.DoesNotContain("/Seasonality", html);
        Assert.DoesNotContain("cot-chart.js", html);
        Assert.DoesNotContain("seasonality.js", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/JournalEntries/SetupReview")).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.ClrType.Name is "CotReport" or "CotImportRun" or "SeasonalityPriceEntity");
        Assert.Equal("MarketPrices", db.Model.FindEntityType(typeof(MarketPriceEntity))!.GetTableName());
    }

    [Fact]
    public async Task JournalCrudAndEmptyStateWorkWithRealForms()
    {
        await using var app = new JournalApp();
        using var client = await app.ClientAsync("owner");
        var list = await client.GetStringAsync("/TradingJournals");
        Assert.Contains("First journal", list);
        Assert.DoesNotContain("Private journal", list);
        var create = await PostAsync(client, "/TradingJournals/Create", new() { ["Name"] = "  New journal  ", ["Description"] = "Description", ["UserId"] = "other", ["Id"] = "99" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Contains("/journals/", create.Headers.Location!.OriginalString);
        int id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.TradingJournals.SingleAsync(j => j.Name == "New journal");
            id = saved.Id;
            Assert.Equal("owner", saved.UserId);
        }
        var empty = await client.GetStringAsync($"/journals/{id}");
        Assert.Contains("New journal", empty);
        var edit = await PostAsync(client, $"/TradingJournals/Edit/{id}", new() { ["Name"] = "Renamed", ["Description"] = "Updated", ["UserId"] = "other" });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        Assert.Contains("Renamed", await client.GetStringAsync("/TradingJournals"));
        var invalid = await PostAsync(client, "/TradingJournals/Create", new() { ["Name"] = "   " });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var delete = await PostAsync(client, $"/TradingJournals/Delete/{id}", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/journals/{id}")).StatusCode);
        using var newUser = await app.ClientAsync("empty");
        Assert.Contains("první deník", WebUtility.HtmlDecode(await newUser.GetStringAsync("/TradingJournals")));
    }

    [Fact]
    public async Task TradesStatisticsImagesAndMutationsAreScopedToJournalAndOwner()
    {
        await using var app = new JournalApp();
        using var client = await app.ClientAsync("owner");
        var first = await client.GetStringAsync("/journals/1");
        Assert.Contains("First journal", first);
        Assert.DoesNotContain("SECOND-TRADE-NOTE", first);
        foreach (var url in new[] { "/journals/1/Create", "/journals/1/Analysis", "/journals/1/Details/1", "/journals/1/Edit/1", "/journals/1/Delete/1" })
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url}: {response.StatusCode}; {await response.Content.ReadAsStringAsync()}");
        }
        var form = await client.GetStringAsync("/journals/1/Create");
        Assert.Contains("action=\"/journals/1/Create\"", form);
        Assert.Contains("/journals/1/Analysis", first);
        foreach (var action in new[] { "Details", "Edit", "Delete", "Image", "GalleryImage" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/journals/1/{action}/2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/journals/2/GalleryImage/2")).StatusCode);
        foreach (var url in new[] { "/journals/3", "/journals/3/Details/3", "/TradingJournals/Edit/3", "/TradingJournals/Delete/3" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        var token = await TokenAsync(client, "/journals/1/Create");
        foreach (var url in new[] { "/journals/1/Delete/2", "/journals/1/DeleteGalleryImage/2", "/journals/1/DeleteLegacyImage/2", "/TradingJournals/Delete/3", "/TradingJournals/Edit/3" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendPostAsync(client, url, token, new())).StatusCode);
        var create = await SendPostAsync(client, "/journals/1/Create", token, new()
        {
            ["JournalId"] = "3", ["UserId"] = "other", ["Asset"] = "XAUUSD", ["Side"] = "Long",
            ["EntryTime"] = "2026-10-31T10:00:00+00:00", ["Status"] = "Open", ["EntryPrice"] = "2000", ["RiskPercentage"] = "1", ["Note"] = "NEW-SCOPED-TRADE"
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.Contains("/journals/1", create.Headers.Location!.OriginalString);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.JournalEntries.SingleAsync(e => e.Note == "NEW-SCOPED-TRADE");
        Assert.Equal(1, saved.JournalId);
        Assert.Equal("owner", saved.UserId);
        Assert.Equal(4, await db.JournalEntries.CountAsync());
        Assert.Equal(2, await db.JournalEntryImages.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/journals/2/Details/{saved.Id}")).StatusCode);
    }

    [Fact]
    public async Task NonemptyJournalsCannotBeDeletedAndOldLinksRemainUsable()
    {
        await using var app = new JournalApp();
        using var client = await app.ClientAsync("owner");
        var response = await PostAsync(client, "/TradingJournals/Delete/1", new());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.TradingJournals.AnyAsync(j => j.Id == 1));
        Assert.True(await db.JournalEntries.AnyAsync(e => e.Id == 1));
        Assert.Equal("/TradingJournals", (await client.GetAsync("/")).Headers.Location!.OriginalString);
        Assert.Equal("/TradingJournals", (await client.GetAsync("/JournalEntries")).Headers.Location!.OriginalString);
        Assert.Equal("/journals/1/Details/1", (await client.GetAsync("/JournalEntries/Details/1")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/TradingJournals/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "No token" }))).StatusCode);
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/TradingJournals")).StatusCode);
    }

    [Fact]
    public async Task TradeResultsRoundTripMoneyEarlyExitsAndSeasonalityWithRealForms()
    {
        await using var app = new JournalApp();
        using var client = await app.ClientAsync("owner");
        var html = await client.GetStringAsync("/journals/1/Create");
        Assert.DoesNotContain("name=\"ExitTime\"", html);
        foreach (var pair in new[] { "USDJPY", "AUDUSD", "NZDCAD", "CHFJPY", "EURCZK", "USDMXN" })
            Assert.Contains(">" + pair + "</option>", html);
        var fields = new Dictionary<string, string>
        {
            ["Asset"] = "EURCZK", ["Side"] = "Short", ["EntryTime"] = "2026-11-05T10:00:00+00:00",
            ["Status"] = "Executed", ["Outcome"] = "Manual", ["ProfitLoss"] = "-1 250,50",
            ["PlannedOutcome"] = "Win", ["SeasonalityAligned"] = "true", ["Note"] = "MONEY-TEST"
        };
        var response = await PostAsync(client, "/journals/1/Create", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        int id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.JournalEntries.SingleAsync(e => e.Note == "MONEY-TEST");
            id = saved.Id;
            Assert.Equal(-1250.50m, saved.ProfitLoss);
            Assert.Equal(TradeOutcome.Manual, saved.Outcome);
            Assert.Equal(PlannedTradeOutcome.Win, saved.PlannedOutcome);
            Assert.True(saved.SeasonalityAligned);
            Assert.Null(saved.ExitTime);
        }
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/journals/1/Details/{id}"));
        Assert.Contains("Zavřeno ručně", details);
        Assert.Contains("Zisk (TP)", details);
        var analysis = WebUtility.HtmlDecode(await client.GetStringAsync("/journals/1/Analysis"));
        Assert.Contains("BE a ruční uzavření", analysis);
        Assert.Contains("Sezónnost a výsledky", analysis);
        Assert.Matches(@"-1[\s,.]?250[,.]50", analysis);
        var index = await client.GetStringAsync("/journals/1?month=2026-11-01&selectedDate=2026-11-05");
        Assert.DoesNotContain("Čas uzavření", WebUtility.HtmlDecode(index));
        Assert.DoesNotContain("-1R", index);
        Assert.Contains("-1250,5", WebUtility.HtmlDecode(index));

        fields["ProfitLoss"] = "123.45";
        fields["Outcome"] = "Breakeven";
        fields["PlannedOutcome"] = "Loss";
        fields["SeasonalityAligned"] = "false";
        response = await PostAsync(client, $"/journals/1/Edit/{id}", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().JournalEntries.FindAsync(id);
            Assert.Equal(123.45m, saved!.ProfitLoss);
            Assert.Equal(PlannedTradeOutcome.Loss, saved.PlannedOutcome);
            Assert.False(saved.SeasonalityAligned);
        }
        fields["ProfitLoss"] = "nonsense";
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/journals/1/Edit/{id}", fields)).StatusCode);
        fields["ProfitLoss"] = "1.234";
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/journals/1/Edit/{id}", fields)).StatusCode);
        fields["ProfitLoss"] = "10";
        fields["Status"] = "Open";
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(client, $"/journals/1/Edit/{id}", fields)).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().JournalEntries.FindAsync(id);
            Assert.Null(saved!.ProfitLoss);
            Assert.Null(saved.PlannedOutcome);
            Assert.Equal(TradeOutcome.Unknown, saved.Outcome);
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string> fields) =>
        await SendPostAsync(client, url, await TokenAsync(client, url), fields);
    private static Task<HttpResponseMessage> SendPostAsync(HttpClient client, string url, string token, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;
        return client.PostAsync(url, new FormUrlEncodedContent(fields));
    }
    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        // Nonempty journals intentionally do not render a destructive form.
        if (!match.Success && url.Contains("/Delete/")) return await TokenAsync(client, "/TradingJournals/Create");
        Assert.True(match.Success, "Antiforgery token missing: " + url);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    internal sealed class JournalApp : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost;Database=Testing;Integrated Security=true");
            builder.UseSetting("DataRefresh:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                connection.Open();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                services.AddScoped<ApplicationDbContext, JournalSqliteContext>();
                foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType?.Namespace?.StartsWith("TradingJournal") == true).ToArray()) services.Remove(service);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "Test"; options.DefaultChallengeScheme = "Test"; })
                    .AddScheme<AuthenticationSchemeOptions, TestIdentity>("Test", _ => { });
            });
        }
        public async Task<HttpClient> ClientAsync(string user)
        {
            var client = CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Test-User", user);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await db.Database.EnsureCreatedAsync())
            {
                db.Users.AddRange(new IdentityUser { Id = "owner", UserName = "Owner" }, new IdentityUser { Id = "other", UserName = "Other" }, new IdentityUser { Id = "empty", UserName = "Empty" });
                db.TradingJournals.AddRange(new TradingJournalBook { Id = 1, UserId = "owner", Name = "First journal" }, new TradingJournalBook { Id = 2, UserId = "owner", Name = "Second journal" }, new TradingJournalBook { Id = 3, UserId = "other", Name = "Private journal" });
                for (var id = 1; id <= 3; id++) db.JournalEntries.Add(new JournalEntry
                {
                    Id = id, JournalId = id, UserId = id == 3 ? "other" : "owner", Asset = AssetSymbol.XAUUSD, EntryTime = DateTimeOffset.UtcNow,
                    Note = id == 2 ? "SECOND-TRADE-NOTE" : "Trade", ImageData = [1, 2, 3], ImageContentType = "image/png", Outcome = TradeOutcome.Win,
                    Images = id < 3 ? [new JournalEntryImage { Id = id, ImageData = [1, 2, 3], ImageContentType = "image/png", ImageFileName = "test.png" }] : []
                });
                await db.SaveChangesAsync();
            }
            return client;
        }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class JournalSqliteContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // SQLite integration tests need an orderable representation of DateTimeOffset.
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }

    private sealed class TestIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].FirstOrDefault();
            if (user is null) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)], IdentityConstants.ApplicationScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
