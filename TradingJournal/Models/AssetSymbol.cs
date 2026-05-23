namespace TradingJournal.Models
{
    public enum AssetSymbol
    {

        // Commodities
        XAUUSD = 1, // Gold
        XAGUSD = 2, // Silver

        // Stocks
        AAPL = 101,
        MSFT = 102,
        AMZN = 103,
        GOOGL = 104,
        TSLA = 105,
        NVDA = 106,
        META = 107,
        SPY = 108,
        QQQ = 109,

        // Crypto
        BTCUSDT = 20,
        ETHUSDT = 21,
        SOLUSDT = 22,
        BNBUSDT = 23,
        XRPUSDT = 24,

        // Forex
        EURUSD = 40,
        GBPUSD = 41,
        USDJPY = 42,

        // Futures (příklady)
        ES = 60,   // S&P 500 e-mini
        MES = 61,  // Micro S&P 500 e-mini
        NQ = 62,   // Nasdaq e-mini
        MNQ = 63,  // Micro Nasdaq e-mini
        US100 = 67, // Nasdaq 100 CFD / cash index
        CL = 64,    // Crude Oil
        GC = 65,    // Gold Futures
        MGC = 66,   // Micro Gold Futures

        
    }
}
