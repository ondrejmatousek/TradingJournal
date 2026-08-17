using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Services;

namespace TradingJournal.Controllers;

[Authorize]
public class CotController : Controller
{
    private readonly CotDataService _cotDataService;
    private readonly ILogger<CotController> _logger;

    public CotController(CotDataService cotDataService, ILogger<CotController> logger)
    {
        _cotDataService = cotDataService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _cotDataService.GetDashboardAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cotDataService.UpdateAsync(force: true, cancellationToken);
            TempData["CotMessage"] = result.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual COT update failed.");
            TempData["CotMessage"] = "COT data se nepodařilo aktualizovat. Zkontroluj log aplikace.";
        }

        return RedirectToAction(nameof(Index));
    }
}
