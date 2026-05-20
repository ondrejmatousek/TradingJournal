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

        public HomeController(
            ILogger<HomeController> logger,
            MarketBiasService marketBiasService,
            StockRadarService stockRadarService)
        {
            _logger = logger;
            _marketBiasService = marketBiasService;
            _stockRadarService = stockRadarService;
        }

        public IActionResult Index()
        {
            return View(new TradingJournal.ViewModels.HomeIndexViewModel());
        }

        [HttpGet]
        public async Task<IActionResult> MarketBiasData(CancellationToken cancellationToken)
        {
            try
            {
                var model = await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken);
                return Json(new
                {
                    updatedAt = model.MarketBiasUpdatedAt?.ToLocalTime().ToString("dd.MM. HH:mm"),
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
                    updatedAt = stockRadar.UpdatedAt?.ToLocalTime().ToString("dd.MM. HH:mm"),
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshMarketBias(CancellationToken cancellationToken)
        {
            try
            {
                var model = await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken, forceRefresh: true);
                return Json(new
                {
                    updatedAt = model.MarketBiasUpdatedAt?.ToLocalTime().ToString("dd.MM. HH:mm"),
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
