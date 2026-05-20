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

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var model = await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken);
            var stockRadar = await _stockRadarService.GetStockRadarAsync(cancellationToken);
            model.StockIdeas = stockRadar.Ideas;
            model.StockRadarUpdatedAt = stockRadar.UpdatedAt;
            model.StockRadarConfigured = stockRadar.IsConfigured;
            model.StockRadarMessage = stockRadar.Message;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshMarketBias(CancellationToken cancellationToken)
        {
            await _marketBiasService.GetHomeMarketBiasAsync(cancellationToken, forceRefresh: true);
            return RedirectToAction(nameof(Index));
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
