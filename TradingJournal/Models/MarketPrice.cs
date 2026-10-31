namespace TradingJournal.Models;

public sealed record MarketAsset(string Key, string Name, string YahooSymbol, string StooqSymbol);
public sealed record MarketPrice(DateOnly Date, decimal Close);
