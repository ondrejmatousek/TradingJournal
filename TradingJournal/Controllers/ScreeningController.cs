using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Services.Screening;

namespace TradingJournal.Controllers;

[Authorize]
public sealed class ScreeningController(InvestmentScreeningService service, ScreeningStore store, ScreeningDataProvider provider,
    ILogger<ScreeningController>? logger = null) : Controller
{
    private string UserKey => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Missing user identity.");
    [HttpGet]
    public IActionResult Index() => View("~/Views/Investments/Screener.cshtml", InvestmentUniverse.All.Count);
    [HttpGet]
    public async Task<IActionResult> UpdateStatus([FromServices] Microsoft.Extensions.Options.IOptions<ScreeningSchedule> schedule, CancellationToken ct)
        => Json(new { schedule = schedule.Value, run = await store.ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, ct) });
    [HttpGet]
    public Task<IActionResult> Data(CancellationToken ct) => ReadScreeningAsync(async () =>
        Json(new { rows = await service.LoadAsync(UserKey, ct), macro = await ContextAsync(ct) }), ct);

    private async Task<IActionResult> ReadScreeningAsync(Func<Task<IActionResult>> read, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await read();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Navigation/reload disconnects the client. Preserve cancellation through EF;
            // do not continue expensive database work or turn it into an application error.
            return StatusCode(499);
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Data.Common.DbException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "Screening data could not be loaded; caller cancellation: {Canceled}.", ct.IsCancellationRequested);
            return StatusCode(503, "Data screeneru se nepodařilo načíst. Zkus načtení zopakovat; uložené výsledky zůstaly zachované.");
        }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(string symbol, CancellationToken ct)
    {
        if (!InvestmentUniverse.All.Any(s => s.Symbol == symbol)) return BadRequest("Neznámý ticker.");
        return await ReadScreeningAsync(async () => Json(await service.RefreshAsync(symbol, UserKey, ct, force: true)), ct);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshMacro(CancellationToken ct)
    {
        try { var macro = await provider.MacroAsync(ct); try { var geo = await provider.GeopoliticsAsync(ct); macro.Add(geo); } catch { } return Json(macro); }
        catch (Exception) when (!ct.IsCancellationRequested) { return StatusCode(503, "Makrodata nejsou dostupná. Původní data zůstala zachována."); }
    }

    private async Task<List<MacroObservation>> ContextAsync(CancellationToken ct)
    {
        var macro = await store.ReadAsync<List<MacroObservation>>("macro-v1", ct) ?? [];
        var geo = await store.ReadAsync<MacroObservation>("macro-geopolitics-v1", ct);
        if (geo != null) macro.Add(geo);
        return macro;
    }
    [HttpGet]
    public async Task<IActionResult> Research(string symbol, CancellationToken ct)
    {
        if (!InvestmentUniverse.All.Any(s => s.Symbol == symbol)) return BadRequest();
        return Json(await store.ReadAsync<ScreeningResearch>("research-" + UserKey + "-" + symbol, ct) ?? new());
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> Research(string symbol, [FromBody] ScreeningResearch research, CancellationToken ct)
    {
        if (!ModelState.IsValid || research == null || !InvestmentUniverse.All.Any(s => s.Symbol == symbol)) return BadRequest("Neplatný ticker nebo formát dat.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        static bool Url(string? value) => value?.Length <= 2000 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo);
        if (research.Evidence == null || research.Evidence.Count > 30 || research.Evidence.Any(e => e == null || e.Pillar is not ("macro" or "geopolitics" or "analysts") || e.Score is < 0 or > 100
            || e.Published > today || e.Published < today.AddYears(-10) || e.HorizonDays is < 1 or > 365 || !Url(e.SourceUrl)
            || string.IsNullOrWhiteSpace(e.Thesis) || e.Thesis.Length > 3000 || string.IsNullOrWhiteSpace(e.Risk) || e.Risk.Length > 3000 || string.IsNullOrWhiteSpace(e.Author) || e.Author.Length > 200))
            return BadRequest("Rozbor potřebuje autora, HTTPS zdroj, platné datum, horizont 1–365 dní, skóre 0–100, tezi a riziko. Maximum 30 rozborů.");
        if (research.Fundamentals is {} f && (f.PeriodEnd > today || f.PeriodEnd < today.AddYears(-10) || f.Filed < f.PeriodEnd || f.Filed > today || f.Currency != "USD" || !Url(f.Source)
            || f.Revenue < 1 || f.PreviousRevenue < 1 || f.Shares < 1 || f.Assets < 1 || f.Liabilities < 0 || f.CapitalExpenditure < 0
            || new[] { f.Revenue, f.PreviousRevenue, f.Shares, f.Assets, f.Liabilities, f.CapitalExpenditure, f.OperatingCashFlow, f.NetIncome }.Any(v => Math.Abs(v) > 1000000000000000m)))
            return BadRequest("Zkontroluj výkazy: USD v absolutních jednotkách, kladné tržby/aktiva/počet akcií, nezáporný capex/závazky, správná data a HTTPS zdroj.");
        await store.WriteAsync("research-" + UserKey + "-" + symbol, research, ct);
        return Ok(new { saved = true });
    }
}


