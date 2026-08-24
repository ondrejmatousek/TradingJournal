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
    public async Task<IActionResult> Data(CancellationToken cancellationToken)
    {
        var model = await _service.LoadCachedAsync(cancellationToken);
        var prices = model.Prices.ToDictionary(pair => pair.Key, pair => pair.Value.Select(item => new
        {
            date = item.Date.ToString("yyyy-MM-dd"),
            close = item.Close
        }));
        return Json(new { prices, isUpdating = _updateState.IsRunning, error = _updateState.LastError });
    }
}
