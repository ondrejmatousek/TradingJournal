using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Services;
using TradingJournal.ViewModels;

namespace TradingJournal.Controllers;

[Authorize]
public sealed class SeasonalityController : Controller
{
    private readonly SeasonalityService _service;
    private readonly SeasonalityUpdateState _updateState;

    public SeasonalityController(SeasonalityService service, SeasonalityUpdateState updateState)
    {
        _service = service;
        _updateState = updateState;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await _service.LoadCachedAsync(cancellationToken);
        return View(new SeasonalityViewModel
        {
            Assets = model.Assets,
            Prices = model.Prices,
            IsUpdating = _updateState.IsRunning
        });
    }

    [HttpGet]
    public async Task<IActionResult> Data(string? assetKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetKey))
        {
            return BadRequest(new { message = "Missing assetKey." });
        }

        var asset = SeasonalityService.Assets.FirstOrDefault(item =>
            string.Equals(item.Key, assetKey, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return NotFound(new { message = "Unknown asset." });
        }

        var prices = await _service.LoadCachedPricesAsync(asset.Key, cancellationToken);
        return Json(new
        {
            assetKey = asset.Key,
            prices = prices.Select(item => new
            {
                date = item.Date.ToString("yyyy-MM-dd"),
                close = item.Close
            }),
            isUpdating = _updateState.IsRunning,
            error = _updateState.LastError
        });
    }
}
