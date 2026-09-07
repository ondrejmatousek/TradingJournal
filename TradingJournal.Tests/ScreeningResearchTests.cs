using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using TradingJournal.Controllers;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;

public sealed class ScreeningResearchTests
{
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "screening-test-" + Guid.NewGuid().ToString("N"));
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static ScreeningController Controller(ScreeningStore store, string user) => new(null!, store, null!)
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test")) } }
    };
    [Fact]
    public async Task Research_IsDurableAndIsolatedBetweenAccounts()
    {
        var env = new TestEnvironment();
        var store = new ScreeningStore(env);
        var data = new ScreeningResearch { Evidence = [new("geopolitics", 60, DateOnly.FromDateTime(DateTime.UtcNow), "https://example.com/source", "Author", "Thesis", "Risk", 30)] };
        Assert.IsType<OkObjectResult>(await Controller(store,"first").Research("MSFT", data, CancellationToken.None));
        var restarted = new ScreeningStore(env);
        var first = Assert.IsType<JsonResult>(await Controller(restarted,"first").Research("MSFT", CancellationToken.None));
        var second = Assert.IsType<JsonResult>(await Controller(restarted,"second").Research("MSFT", CancellationToken.None));
        Assert.Single(Assert.IsType<ScreeningResearch>(first.Value).Evidence);
        Assert.Empty(Assert.IsType<ScreeningResearch>(second.Value).Evidence);
    }
    [Fact]
    public async Task Research_RejectsUnsafeSourcesAndUnknownSymbols()
    {
        var controller = Controller(new ScreeningStore(new TestEnvironment()),"first");
        var data = new ScreeningResearch { Evidence = [new("geopolitics", 60, DateOnly.FromDateTime(DateTime.UtcNow), "javascript:alert(1)", "Author", "Thesis", "Risk", 30)] };
        Assert.IsType<BadRequestObjectResult>(await controller.Research("MSFT",data,CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Research("../../private",new(),CancellationToken.None));
    }
}
