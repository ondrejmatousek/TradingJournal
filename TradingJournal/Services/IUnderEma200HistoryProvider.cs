namespace TradingJournal.Services
{
    public interface IUnderEma200HistoryProvider
    {
        Task<IReadOnlyList<UnderEma200PricePoint>> GetDailyPricesAsync(string symbol, CancellationToken cancellationToken);
    }
}
