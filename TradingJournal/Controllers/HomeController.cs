using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Models;
using TradingJournal.Services;

namespace TradingJournal.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly MarketBiasService _marketBiasService;
        private readonly StockRadarService _stockRadarService;
        private readonly HomeInsightService _homeInsightService;
        private readonly AppClock _clock;

        public HomeController(
            ILogger<HomeController> logger,
            MarketBiasService marketBiasService,
            StockRadarService stockRadarService,
            HomeInsightService homeInsightService,
            AppClock clock)
        {
            _logger = logger;
            _marketBiasService = marketBiasService;
            _stockRadarService = stockRadarService;
            _homeInsightService = homeInsightService;
            _clock = clock;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> MarketBiasData(CancellationToken cancellationToken)
        {
            try
            {
                var model = await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken);
                return Json(new
                {
                    updatedAt = model.MarketBiasUpdatedAt is null ? null : _clock.FormatShort(model.MarketBiasUpdatedAt.Value),
                    items = model.MarketBiases
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load market bias widget.");
                return Json(new
                {
                    updatedAt = (string?)null,
                    items = Array.Empty<object>(),
                    message = "Market bias se ted nepodarilo nacist."
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> StockRadarData(CancellationToken cancellationToken)
        {
            try
            {
                var stockRadar = await _stockRadarService.GetStockRadarAsync(cancellationToken);
                return Json(new
                {
                    updatedAt = stockRadar.UpdatedAt is null ? null : _clock.FormatShort(stockRadar.UpdatedAt.Value),
                    message = stockRadar.Message,
                    items = stockRadar.Ideas
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load stock radar widget.");
                return Json(new
                {
                    updatedAt = (string?)null,
                    items = Array.Empty<object>(),
                    message = "Stock radar se ted nepodarilo nacist."
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> MarketRegimeData(CancellationToken cancellationToken)
        {
            try
            {
                var regime = await _homeInsightService.GetMarketRegimeAsync(cancellationToken);
                return Json(regime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load market regime widget.");
                return Json(new
                {
                    regime = "Mixed",
                    tone = "neutral",
                    score = 0,
                    summary = "Trzni rezim se ted nepodarilo nacist.",
                    drivers = Array.Empty<string>()
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> EconomicCalendarData(CancellationToken cancellationToken)
        {
            try
            {
                return Json(new
                {
                    updatedAt = _clock.Now.ToString("dd.MM. HH:mm"),
                    items = await _homeInsightService.GetEconomicCalendarAsync(cancellationToken)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load economic calendar widget.");
                return Json(new
                {
                    updatedAt = (string?)null,
                    items = Array.Empty<object>(),
                    message = "Makro kalendar se ted nepodarilo nacist."
                });
            }
        }

        [HttpGet]
        public IActionResult SetupWatchlistData()
        {
            return Json(new
            {
                updatedAt = _clock.Now.ToString("dd.MM. HH:mm"),
                items = _homeInsightService.GetSetupWatchlist()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshMarketBias(CancellationToken cancellationToken)
        {
            try
            {
                var model = await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken, forceRefresh: true);
                return Json(new
                {
                    updatedAt = model.MarketBiasUpdatedAt is null ? null : _clock.FormatShort(model.MarketBiasUpdatedAt.Value),
                    items = model.MarketBiases
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh market bias widget.");
                return Json(new
                {
                    updatedAt = (string?)null,
                    items = Array.Empty<object>(),
                    message = "Market bias se ted nepodarilo obnovit."
                });
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
