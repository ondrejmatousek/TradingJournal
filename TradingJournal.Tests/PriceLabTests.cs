using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TradingJournal.Services.Screening;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Tests;

public class PriceLabTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);
    private static ValuationReport Report(ValuationForecast? forecast = null) => ValuationV2Tests.Engine()
        .Calculate("TEST", "Technology", ValuationV2Tests.Dataset(), 15, Today.AddDays(-1), Today, null, forecast);

    [Fact]
    public void EquityReturnDiscountsCashAfterInterestAndDoesNotSubtractDebtTwice()
    {
        var report = Report();
        report.Assumptions = [new("Tax", 0, "ratio", "fixture", Today, false, ""), new("CostOfDebt", .05m, "ratio", "fixture", Today, false, "")];
        var scenario = report.Scenarios[1] with
        {
            Debt = 100, ExcessCash = 20, Investments = 10, OtherClaims = 0, Shares = 10, TerminalGrowth = 0, TerminalFcff = 100,
            Years = Enumerable.Range(1, 10).Select(y => new ForecastYear(y, 0, 100, 1, 100, 0, 100, 0, 0, 0, 100, 0, 0)).ToList()
        };
        // 100 FCFF minus 5 interest forever at 10%, plus 30 nonoperating assets, divided by 10 shares.
        Assert.InRange(Math.Abs(PriceLabMath.ReturnPrice(report, scenario, .10m)!.Value - 98), 0, .00000001m);
        Assert.True(PriceLabMath.ReturnPrice(report, scenario, .15m) < PriceLabMath.ReturnPrice(report, scenario, .10m));
        Assert.Null(PriceLabMath.ReturnPrice(report, scenario with { TerminalGrowth = .10m }, .10m));
    }

    [Fact]
    public void DebtFreeReturnPriceReconcilesToDcfAtSameRate()
    {
        var report = Report(); var scenario = report.Scenarios[1] with { Debt = 0 };
        var expected = (scenario.PvExplicit + scenario.PvTerminal + scenario.ExcessCash + scenario.Investments - scenario.OtherClaims) / scenario.Shares;
        Assert.InRange(Math.Abs(PriceLabMath.ReturnPrice(report, scenario, scenario.Wacc)!.Value - expected), 0, .00000001m);
    }

    [Fact]
    public void ReverseDcfRecoversGrowthAndRefusesAmbiguousOrUnboundedAnswers()
    {
        var template = Report().Scenarios[1];
        var solved = PriceLabMath.Reverse(118, g => template with { FairValue = 100 + 200 * g });
        Assert.Equal("solved", solved.Status); Assert.InRange(solved.InitialGrowthPercent!.Value, 8.999m, 9.001m);
        Assert.Equal("outside-range", PriceLabMath.Reverse(1000, g => template with { FairValue = 100 + 200 * g }).Status);
        Assert.Equal("ambiguous", PriceLabMath.Reverse(100, g => template with { FairValue = 100 + 100 * (g - .10m) * (g - .30m) }).Status);
        Assert.Equal("unavailable", PriceLabMath.Reverse(100, _ => null).Status);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => PriceLabMath.Reverse(118, _ => template, cancel.Token));
    }

    [Fact]
    public void SourcedForecastChangesGrowthMarginAndRiskWithoutChangingInputDataset()
    {
        var forecast = new ValuationForecast(.30m, .35m, 1.2m, "https://example.com/guidance", Today);
        var old = Report(); var modified = Report(forecast);
        Assert.NotNull(modified.BaseFairValue);
        Assert.Equal(.30m, modified.Scenarios[1].Years[0].RevenueGrowth);
        Assert.Equal(.35m, modified.Scenarios[1].Years[^1].EbitMargin);
        Assert.Contains(modified.Assumptions, a => a.Name == "Beta" && a.Value == 1.2m && a.Source == forecast.Source);
        Assert.Equal(old.InputHash, modified.InputHash); Assert.NotEqual(old.Id, modified.Id);
        Assert.Equal("InvalidData", Report(forecast with { InitialGrowth = .9m }).Status);
        Assert.Equal("InvalidData", Report(forecast with { Date = Today.AddDays(1) }).Status);
    }

    [Fact]
    public void ForecastRequiresDatedSafeSourceAndPairedComparisonInputs()
    {
        Assert.Null(new PriceLabSettings().Validate(Today));
        Assert.NotNull(new PriceLabSettings { GrowthPercent = 12 }.Validate(Today));
        Assert.NotNull(new PriceLabSettings { SourceUrl = "javascript:alert(1)" }.Validate(Today));
        Assert.NotNull(new PriceLabSettings { ForwardEps = 5, SourceUrl = "https://example.com", SourceDate = Today }.Validate(Today));
        Assert.NotNull(new PriceLabSettings { RequiredReturnPercent = 0 }.Validate(Today));
        Assert.Null(new PriceLabSettings { GrowthPercent = 12, ForwardEps = 5, ReferencePe = 20, SourceUrl = "https://example.com", SourceDate = Today }.Validate(Today));
    }

    [Fact]
    public async Task RealRoutesRenderValidateCsrfAndPersistForecastsOnlyForTheirOwner()
    {
        await using var app = new MultipleJournalsTests.JournalApp();
        using var owner = await app.ClientAsync("owner");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ScreeningStore>();
            await store.WriteAsync("market-MSFT", new ScreeningMarketCache(DateTimeOffset.UtcNow, [new(today, 15)], null, "fixture") { Financials = ValuationV2Tests.Dataset() }, default);
            await store.WriteAsync("macro-v1", new List<MacroObservation> { new("DGS10", today, 4.5m, null, "fixture") }, default);
        }
        var html = await owner.GetStringAsync("/Screening");
        Assert.Contains("price-lab-dialog", html); Assert.Contains("price-lab.js", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var initial = await owner.GetFromJsonAsync<JsonElement>("/Screening/PriceLab?symbol=MSFT");
        Assert.Equal(10, initial.GetProperty("rows")[0].GetProperty("settings").GetProperty("requiredReturnPercent").GetDecimal());
        Assert.True(initial.GetProperty("rows")[0].GetProperty("returnPrice").GetDecimal() > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/Screening/PriceLab?symbol=MSFT", new PriceLabSettings())).StatusCode);
        async Task<HttpResponseMessage> Save(PriceLabSettings settings)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/Screening/PriceLab?symbol=MSFT") { Content = JsonContent.Create(settings) };
            message.Headers.Add("RequestVerificationToken", token); return await owner.SendAsync(message);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(new() { GrowthPercent = 40 })).StatusCode);
        var saved = await Save(new() { RequiredReturnPercent = 15 }); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("returnPrice").GetDecimal() < initial.GetProperty("rows")[0].GetProperty("returnPrice").GetDecimal());
        var again = await owner.GetFromJsonAsync<JsonElement>("/Screening/PriceLab?symbol=MSFT");
        Assert.Equal(15, again.GetProperty("rows")[0].GetProperty("settings").GetProperty("requiredReturnPercent").GetDecimal());
        var comparison = await Save(new() { RequiredReturnPercent = 15, GrowthPercent = 12, MarginPercent = 30, Beta = 1.2m,
            ForwardEps = 7, ReferencePe = 20, SourceUrl = "https://example.com/forecast", SourceDate = today });
        Assert.Equal(HttpStatusCode.OK, comparison.StatusCode);
        var compared = await comparison.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(140, compared.GetProperty("multipleValue").GetDecimal());
        Assert.True(compared.GetProperty("reviewRequired").GetBoolean());
        Assert.Equal("https://example.com/fixture", compared.GetProperty("financialSources")[0].GetString());
        var reset = await Save(new()); Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var restored = await reset.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, restored.GetProperty("settings").GetProperty("requiredReturnPercent").GetDecimal());
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("settings").GetProperty("growthPercent").ValueKind);
        using var other = await app.ClientAsync("other");
        var separate = await other.GetFromJsonAsync<JsonElement>("/Screening/PriceLab?symbol=MSFT");
        Assert.Equal(10, separate.GetProperty("rows")[0].GetProperty("settings").GetProperty("requiredReturnPercent").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/Screening/PriceLab?symbol=UNKNOWN")).StatusCode);
        var pilot = await owner.GetFromJsonAsync<JsonElement>("/Screening/PriceLab"); Assert.Equal(5, pilot.GetProperty("rows").GetArrayLength());
    }
}
