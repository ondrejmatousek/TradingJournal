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
    public Task<IActionResult> PriceLab([FromServices] TradingJournal.Services.Valuation.PriceLabService lab,
        string? symbol, CancellationToken ct) => ReadScreeningAsync(async () =>
    {
        var symbols = string.IsNullOrWhiteSpace(symbol) ? TradingJournal.Services.Valuation.PriceLabService.PilotSymbols : new[] { symbol.ToUpperInvariant() };
        var universe = await service.UniverseAsync(ct);
        if (symbols.Any(s => !universe.Any(u => u.Symbol == s))) return BadRequest("Neznámý ticker.");
        try
        {
            var results = new List<TradingJournal.Services.Valuation.PriceLabResult>();
            foreach (var ticker in symbols) results.Add(await lab.AnalyseAsync(UserKey, ticker, ct));
            return Json(new { checkedAt = DateTimeOffset.UtcNow, rows = results });
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }, ct);

    [HttpPost, ActionName("PriceLab"), ValidateAntiForgeryToken, RequestSizeLimit(8192)]
    public Task<IActionResult> SavePriceLab(string symbol, [FromBody] TradingJournal.Services.Valuation.PriceLabSettings settings,
        [FromServices] TradingJournal.Services.Valuation.PriceLabService lab, CancellationToken ct) => ReadScreeningAsync(async () =>
    {
        if (!ModelState.IsValid || settings == null) return BadRequest("Neplatné předpoklady ocenění.");
        var error = settings.Validate(DateOnly.FromDateTime(DateTime.UtcNow));
        if (error != null) return BadRequest(error);
        if (string.IsNullOrWhiteSpace(symbol)) return BadRequest("Chybí ticker.");
        symbol = symbol.ToUpperInvariant();
        try
        {
            var result = await lab.AnalyseAsync(UserKey, symbol, ct, settings);
            await lab.SaveAsync(UserKey, symbol, settings, ct);
            return Json(result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }, ct);

    [HttpGet]
    public Task<IActionResult> Coverage(CancellationToken ct) => ReadScreeningAsync(async () =>
    {
        var rows = await service.AuditAsync(ct);
        return Json(new { checkedAt = DateTimeOffset.UtcNow, total = rows.Count, ready = rows.Count(r => r.Ready),
            available = new[] { "price", "history", "financials", "valuation" }.ToDictionary(key => key,
                key => rows.Count(r => r.Checks.Any(c => c.Key == key && c.Status == "available"))), rows });
    }, ct);
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View("~/Views/Investments/Screener.cshtml", (await service.UniverseAsync(ct)).Count);
    [HttpGet]
    public async Task<IActionResult> UpdateStatus([FromServices] Microsoft.Extensions.Options.IOptions<ScreeningSchedule> schedule, CancellationToken ct)
        => Json(new { schedule = schedule.Value, run = await store.ReadAsync<ScreeningUpdateRun>(ScreeningUpdateHostedService.StateKey, ct) });
    [HttpGet]
    public Task<IActionResult> Data(CancellationToken ct = default, int page = 1, int pageSize = 25,
        string? search = null, string? sector = null, string? filter = null, string? sort = null,
        decimal? minDiscount = null, decimal? minScore = null, decimal? minConfidence = null) => ReadScreeningAsync(async () =>
    {
        var snapshot = await store.ReadAsync<ScreeningSnapshot>(ScreeningSnapshot.Key, ct);
        if (snapshot == null) return StatusCode(503, "Hodnocení zatím není připravené. Spusť denní aktualizaci dat v GitHub Actions.");
        var personal = await store.ReadAsync<ScreeningSnapshot>("screening-personal-" + UserKey, ct);
        if (personal != null && personal.CalculatedAt >= snapshot.CalculatedAt)
            snapshot = snapshot with { Rows = snapshot.Rows.Select(r => personal.Rows.FirstOrDefault(p => p.Symbol == r.Symbol) ?? r).ToList() };
        return Json(ScreeningSnapshot.Page(snapshot, page, pageSize, search, sector, filter, sort, minDiscount, minScore, minConfidence));
    }, ct);

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
        if (!(await service.UniverseAsync(ct)).Any(s => s.Symbol == symbol)) return BadRequest("Neznámý ticker.");
        return await ReadScreeningAsync(async () => Json(await SavePersonalResultAsync(symbol, ct, true)), ct);
    }
    private async Task<ScreeningRow> SavePersonalResultAsync(string symbol, CancellationToken ct, bool refresh)
    {
        var row = refresh ? await service.RefreshAsync(symbol, UserKey, ct, force: true)
            : (await service.LoadAsync(UserKey, ct, [symbol])).Single();
        var shared = await store.ReadAsync<ScreeningSnapshot>(ScreeningSnapshot.Key, ct);
        var personal = await store.ReadAsync<ScreeningSnapshot>("screening-personal-" + UserKey, ct);
        var rows = personal != null && (shared == null || personal.CalculatedAt >= shared.CalculatedAt) ? personal.Rows : [];
        rows.RemoveAll(r => r.Symbol == symbol); rows.Add(ScreeningSnapshot.Compact(row));
        await store.WriteAsync("screening-personal-" + UserKey, new ScreeningSnapshot(DateTimeOffset.UtcNow, rows, []), ct);
        return row;
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
        if (!(await service.UniverseAsync(ct)).Any(s => s.Symbol == symbol)) return BadRequest();
        return Json(await store.ReadAsync<ScreeningResearch>("research-" + UserKey + "-" + symbol, ct) ?? new());
    }

    [HttpGet]
    public async Task<IActionResult> ValuationDetail(string symbol, string id, [FromServices] TradingJournal.Services.Valuation.ValuationPersistence persistence, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id) || id.Length!=64 || !(await service.UniverseAsync(ct)).Any(s=>s.Symbol==symbol)) return BadRequest();
        var report=await persistence.ReadAsync(id,ct);
        return report==null || report.Symbol!=symbol ? NotFound() : Json(new {report,financials=await persistence.InputAsync(report.InputHash,ct)});
    }

    [HttpGet]
    public async Task<IActionResult> ValuationHistory(string symbol, [FromServices] TradingJournal.Services.Valuation.ValuationPersistence persistence, int days = 365, CancellationToken ct = default)
    {
        if (!(await service.UniverseAsync(ct)).Any(s => s.Symbol == symbol)) return BadRequest();
        days = Math.Clamp(days, 1, 3650);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sqlHistory = await store.ReadValuationsAsync(symbol, today.AddDays(-days), ct);
        var v2History=await persistence.HistoryAsync(symbol,DateTimeOffset.UtcNow.AddDays(-days),ct);
        if (v2History.Count>0 || sqlHistory is {Count:>0})
            return Json((sqlHistory??[]).Select(x=>new {date=x.ValuationDate.ToString("yyyy-MM-dd"),fairValueBase=(decimal?)x.FairValueBase,model="Legacy: "+x.Model})
                .Concat(v2History.Select(x=>new {date=x.CreatedAt.ToString("yyyy-MM-dd"),fairValueBase=x.BaseFairValue,model=x.ModelVersion})).OrderBy(x=>x.date));
        var history = new List<(DateOnly date, ValuationResult value)>();
        for (var i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var value = await store.ReadAsync<ValuationResult>($"valuation-history-{symbol}-{date:yyyyMMdd}", ct);
            if (value != null) history.Add((date, value));
        }
        return Json(history.OrderBy(x => x.date).Select(x => new { date = x.date, value = x.value }));
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> Research(string symbol, [FromBody] ScreeningResearch research, CancellationToken ct)
    {
        if (!ModelState.IsValid || research == null || !(await service.UniverseAsync(ct)).Any(s => s.Symbol == symbol)) return BadRequest("Neplatný ticker nebo formát dat.");
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
        if (await store.ReadAsync<ScreeningSnapshot>(ScreeningSnapshot.Key, ct) != null)
            await SavePersonalResultAsync(symbol, ct, false);
        return Ok(new { saved = true });
    }
}


