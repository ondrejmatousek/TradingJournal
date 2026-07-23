using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Services;

namespace TradingJournal.Controllers
{
    [Authorize]
    public class InvestmentsController : Controller
    {
        private readonly UnderEma200Service _underEma200Service;
        private readonly AppClock _clock;
        private readonly ILogger<InvestmentsController> _logger;

        public InvestmentsController(
            UnderEma200Service underEma200Service,
            AppClock clock,
            ILogger<InvestmentsController> logger)
        {
            _underEma200Service = underEma200Service;
            _clock = clock;
            _logger = logger;
        }

        public async Task<IActionResult> UnderEma200(string? timeframe, CancellationToken cancellationToken)
        {
            var result = await _underEma200Service.GetPersistedResultAsync(timeframe, cancellationToken);
            return View(result);
        }

        [HttpGet]
        public async Task<IActionResult> UnderEma200Data(string? timeframe, bool refresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = refresh
                    ? await _underEma200Service.RefreshAsync(timeframe, cancellationToken)
                    : await _underEma200Service.GetPersistedResultAsync(timeframe, cancellationToken);
                return Json(new
                {
                    updatedAt = result.UpdatedAt is null ? null : _clock.FormatShort(result.UpdatedAt.Value),
                    result.Message,
                    result.Timeframe,
                    result.TimeframeLabel,
                    result.EmaLabel,
                    result.Items
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load under EMA200 investment screen.");
                return Json(new
                {
                    updatedAt = (string?)null,
                    message = "Screening pod EMA200 se ted nepodarilo nacist.",
                    items = Array.Empty<object>()
                });
            }
        }
    }
}
