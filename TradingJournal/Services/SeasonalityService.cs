using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Services;

public sealed class SeasonalityService
{
    public static IReadOnlyList<SeasonalityAsset> Assets { get; } =
    [
        new("EURUSD", "EUR/USD", "EURUSD=X", "eurusd"),
        new("GBPUSD", "GBP/USD", "GBPUSD=X", "gbpusd"),
        new("USDJPY", "USD/JPY", "JPY=X", "usdjpy"),
        new("USDCHF", "USD/CHF", "CHF=X", "usdchf"),
        new("AUDUSD", "AUD/USD", "AUDUSD=X", "audusd"),
        new("NZDUSD", "NZD/USD", "NZDUSD=X", "nzdusd"),
        new("USDCAD", "USD/CAD", "CAD=X", "usdcad"),
        new("XAUUSD", "Gold / XAU/USD", "GC=F", "xauusd"),
        new("SILVER", "Silver", "SI=F", "si.f"),
        new("COPPER", "Copper", "HG=F", "hg.f"),
        new("PLATINUM", "Platinum", "PL=F", "pl.f"),
        new("PALLADIUM", "Palladium", "PA=F", "pa.f"),
        new("WTI", "Crude Oil WTI", "CL=F", "cl.f"),
        new("BRENT", "Brent Crude Oil", "BZ=F", "bz.f"),
        new("NATGAS", "Natural Gas", "NG=F", "ng.f"),
        new("CORN", "Corn", "ZC=F", "zc.f"),
        new("WHEAT", "Wheat", "ZW=F", "zw.f"),
        new("SOYBEANS", "Soybeans", "ZS=F", "zs.f"),
        new("COFFEE", "Coffee", "KC=F", "kc.f"),
        new("SUGAR", "Sugar", "SB=F", "sb.f"),
        new("BTCUSD", "Bitcoin / BTC/USD", "BTC-USD", "btcusd"),
        new("ETHUSD", "Ethereum / ETH/USD", "ETH-USD", "ethusd"),
        new("SOLUSD", "Solana / SOL/USD", "SOL-USD", "solusd"),
        new("XRPUSD", "XRP / XRP/USD", "XRP-USD", "xrpusd"),
        new("BNBUSD", "BNB / BNB/USD", "BNB-USD", "bnbusd"),
        new("SPX", "S&P 500", "^GSPC", "^spx"),
        new("NDX", "Nasdaq-100", "^NDX", "^ndx"),
        new("DJI", "Dow Jones Industrial Average", "^DJI", "^dji"),
        new("RUT", "Russell 2000", "^RUT", "^rut"),
        new("VIX", "CBOE Volatility Index", "^VIX", "^vix"),
        new("DAX", "DAX", "^GDAXI", "^dax"),
        new("FTSE100", "FTSE 100", "^FTSE", "^ftse"),
        new("NIKKEI225", "Nikkei 225", "^N225", "^nkx"),
        new("HSI", "Hang Seng Index", "^HSI", "^hsi"),
        new("STOXX50E", "Euro Stoxx 50", "^STOXX50E", "^stoxx50e"),
        new("SPY", "SPDR S&P 500 ETF", "SPY", "spy.us"),
        new("QQQ", "Invesco QQQ ETF", "QQQ", "qqq.us"),
        new("DIA", "SPDR Dow Jones Industrial Average ETF", "DIA", "dia.us"),
        new("IWM", "iShares Russell 2000 ETF", "IWM", "iwm.us"),
        new("VTI", "Vanguard Total Stock Market ETF", "VTI", "vti.us"),
        new("VT", "Vanguard Total World Stock ETF", "VT", "vt.us"),
        new("EFA", "iShares MSCI EAFE ETF", "EFA", "efa.us"),
        new("EEM", "iShares MSCI Emerging Markets ETF", "EEM", "eem.us"),
        new("TLT", "iShares 20+ Year Treasury Bond ETF", "TLT", "tlt.us"),
        new("IEF", "iShares 7-10 Year Treasury Bond ETF", "IEF", "ief.us"),
        new("GLD", "SPDR Gold Shares ETF", "GLD", "gld.us"),
        new("SLV", "iShares Silver Trust ETF", "SLV", "slv.us"),
        new("USO", "United States Oil Fund", "USO", "uso.us"),
        new("UNG", "United States Natural Gas Fund", "UNG", "ung.us"),
        new("XLE", "Energy Select Sector SPDR ETF", "XLE", "xle.us"),
        new("XLF", "Financial Select Sector SPDR ETF", "XLF", "xlf.us"),
        new("XLK", "Technology Select Sector SPDR ETF", "XLK", "xlk.us"),
        new("XLV", "Health Care Select Sector SPDR ETF", "XLV", "xlv.us"),
        new("XLY", "Consumer Discretionary Select Sector SPDR ETF", "XLY", "xly.us"),
        new("XLP", "Consumer Staples Select Sector SPDR ETF", "XLP", "xlp.us"),
        new("XLI", "Industrial Select Sector SPDR ETF", "XLI", "xli.us"),
        new("XLU", "Utilities Select Sector SPDR ETF", "XLU", "xlu.us"),
        new("XLB", "Materials Select Sector SPDR ETF", "XLB", "xlb.us"),
        new("VNQ", "Vanguard Real Estate ETF", "VNQ", "vnq.us"),
        new("ARKK", "ARK Innovation ETF", "ARKK", "arkk.us"),
        new("AAPL", "Apple", "AAPL", "aapl.us"),
        new("MSFT", "Microsoft", "MSFT", "msft.us"),
        new("NVDA", "NVIDIA", "NVDA", "nvda.us"),
        new("AMZN", "Amazon", "AMZN", "amzn.us"),
        new("GOOGL", "Alphabet", "GOOGL", "googl.us"),
        new("META", "Meta Platforms", "META", "meta.us"),
        new("TSLA", "Tesla", "TSLA", "tsla.us"),
        new("AVGO", "Broadcom", "AVGO", "avgo.us"),
        new("ORCL", "Oracle", "ORCL", "orcl.us"),
        new("AMD", "Advanced Micro Devices", "AMD", "amd.us"),
        new("INTC", "Intel", "INTC", "intc.us"),
        new("QCOM", "Qualcomm", "QCOM", "qcom.us"),
        new("CSCO", "Cisco", "CSCO", "csco.us"),
        new("ADBE", "Adobe", "ADBE", "adbe.us"),
        new("CRM", "Salesforce", "CRM", "crm.us"),
        new("NFLX", "Netflix", "NFLX", "nflx.us"),
        new("NOW", "ServiceNow", "NOW", "now.us"),
        new("IBM", "IBM", "IBM", "ibm.us"),
        new("SHOP", "Shopify", "SHOP", "shop.us"),
        new("UBER", "Uber", "UBER", "uber.us"),
        new("PANW", "Palo Alto Networks", "PANW", "panw.us"),
        new("SNOW", "Snowflake", "SNOW", "snow.us"),
        new("PLTR", "Palantir", "PLTR", "pltr.us"),
        new("MU", "Micron Technology", "MU", "mu.us"),
        new("TSM", "Taiwan Semiconductor Manufacturing", "TSM", "tsm.us"),
        new("JPM", "JPMorgan Chase", "JPM", "jpm.us"),
        new("BAC", "Bank of America", "BAC", "bac.us"),
        new("GS", "Goldman Sachs", "GS", "gs.us"),
        new("V", "Visa", "V", "v.us"),
        new("MA", "Mastercard", "MA", "ma.us"),
        new("UNH", "UnitedHealth Group", "UNH", "unh.us"),
        new("JNJ", "Johnson & Johnson", "JNJ", "jnj.us"),
        new("LLY", "Eli Lilly", "LLY", "lly.us"),
        new("PFE", "Pfizer", "PFE", "pfe.us"),
        new("XOM", "Exxon Mobil", "XOM", "xom.us"),
        new("CVX", "Chevron", "CVX", "cvx.us"),
        new("COST", "Costco", "COST", "cost.us"),
        new("WMT", "Walmart", "WMT", "wmt.us"),
        new("MCD", "McDonald's", "MCD", "mcd.us"),
        new("DIS", "Disney", "DIS", "dis.us")
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SeasonalityService> _logger;
    private readonly ApplicationDbContext? _context;

    public SeasonalityService(IHttpClientFactory httpClientFactory, ILogger<SeasonalityService> logger)
        : this(httpClientFactory, logger, null)
    {
    }

    public SeasonalityService(IHttpClientFactory httpClientFactory, ILogger<SeasonalityService> logger, ApplicationDbContext? context)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _context = context;
    }

    public async Task<SeasonalityViewModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_context is not null)
        {
            return new SeasonalityViewModel { Assets = Assets, Prices = await LoadCachedPricesAsync(cancellationToken) };
        }

        return await DownloadAsync(cancellationToken);
    }

    public async Task<SeasonalityViewModel> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        return new SeasonalityViewModel
        {
            Assets = Assets,
            Prices = _context is null ? new Dictionary<string, IReadOnlyList<SeasonalityPrice>>() : await LoadCachedPricesAsync(cancellationToken)
        };
    }

    public async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null) throw new InvalidOperationException("Seasonality database context is not configured.");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingJournal-Seasonality/1.0");
        foreach (var asset in Assets)
        {
            var cached = await _context.SeasonalityPrices
                .Where(item => item.AssetKey == asset.Key)
                .ToDictionaryAsync(item => item.Date, cancellationToken);
            var period1 = cached.Count == 0
                ? 0
                : new DateTimeOffset(cached.Keys.Max().ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1).ToUnixTimeSeconds();
            try
            {
                var yahoo = await client.GetStringAsync(
                    $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(asset.YahooSymbol)}?period1={period1}&period2={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}&interval=1d&events=history",
                    cancellationToken);
                await UpsertAsync(asset.Key, "Yahoo", ParseYahoo(yahoo), cached, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yahoo seasonality data load failed for {Asset}; trying Stooq.", asset.Key);
                try
                {
                    var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={asset.StooqSymbol}&i=d", cancellationToken);
                    await UpsertAsync(asset.Key, "Stooq", ParseStooq(csv), cached, cancellationToken);
                }
                catch (Exception fallbackException)
                {
                    _logger.LogError(fallbackException, "Seasonality data load failed for {Asset}.", asset.Key);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<SeasonalityViewModel> DownloadAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingJournal-Seasonality/1.0");
        var prices = new Dictionary<string, IReadOnlyList<SeasonalityPrice>>();
        foreach (var asset in Assets)
        {
            try
            {
                var yahoo = await client.GetStringAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(asset.YahooSymbol)}?period1=0&period2={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}&interval=1d&events=history", cancellationToken);
                prices[asset.Key] = ParseYahoo(yahoo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Yahoo seasonality data load failed for {Asset}; trying Stooq.", asset.Key);
                try
                {
                    var csv = await client.GetStringAsync($"https://stooq.com/q/d/l/?s={asset.StooqSymbol}&i=d", cancellationToken);
                    prices[asset.Key] = ParseStooq(csv);
                }
                catch (Exception fallbackException)
                {
                    _logger.LogError(fallbackException, "Seasonality data load failed for {Asset}.", asset.Key);
                    prices[asset.Key] = [];
                }
            }
        }
        return new SeasonalityViewModel { Assets = Assets, Prices = prices };
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<SeasonalityPrice>>> LoadCachedPricesAsync(CancellationToken cancellationToken)
    {
        var rows = await _context!.SeasonalityPrices.AsNoTracking().OrderBy(item => item.Date).ToListAsync(cancellationToken);
        return rows.GroupBy(item => item.AssetKey).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<SeasonalityPrice>)group.Select(item => new SeasonalityPrice(item.Date, item.Close)).ToList());
    }

    private async Task UpsertAsync(string assetKey, string source, IReadOnlyList<SeasonalityPrice> prices, Dictionary<DateOnly, SeasonalityPriceEntity> cached, CancellationToken cancellationToken)
    {
        foreach (var price in prices)
        {
            if (cached.TryGetValue(price.Date, out var existing))
            {
                existing.Close = price.Close;
                existing.Source = source;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _context!.SeasonalityPrices.Add(new SeasonalityPriceEntity
                {
                    AssetKey = assetKey,
                    Date = price.Date,
                    Close = price.Close,
                    Source = source,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }
        await _context!.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<SeasonalityPrice> ParseStooq(string csv)
    {
        var result = new List<SeasonalityPrice>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var fields = line.Trim().Split(',');
            if (fields.Length < 5 || !DateOnly.TryParse(fields[0], CultureInfo.InvariantCulture, out var date) ||
                !decimal.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                continue;
            result.Add(new SeasonalityPrice(date, close));
        }
        return result.OrderBy(item => item.Date).ToList();
    }

    public static IReadOnlyList<SeasonalityPrice> ParseYahoo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<SeasonalityPrice>();
        var chart = document.RootElement.GetProperty("chart").GetProperty("result")[0];
        var timestamps = chart.GetProperty("timestamp");
        var closes = chart.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
        for (var index = 0; index < timestamps.GetArrayLength(); index++)
        {
            if (timestamps[index].ValueKind != JsonValueKind.Number || closes[index].ValueKind is JsonValueKind.Null)
                continue;
            result.Add(new SeasonalityPrice(
                DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[index].GetInt64()).UtcDateTime),
                closes[index].GetDecimal()));
        }
        return result.Where(item => item.Close > 0).DistinctBy(item => item.Date).OrderBy(item => item.Date).ToList();
    }
}
