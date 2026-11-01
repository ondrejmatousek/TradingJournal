namespace TradingJournal.Services
{
    public interface IUnderEma200HistoryProvider
    {
        Task<IReadOnlyList<UnderEma200PricePoint>> GetDailyPricesAsync(string symbol, CancellationToken cancellationToken);
        async Task<PriceHistorySnapshot> GetSnapshotAsync(string symbol, CancellationToken cancellationToken)
            => new(await GetDailyPricesAsync(symbol, cancellationToken), null, []);
    }
    public sealed record PriceHistorySnapshot(IReadOnlyList<UnderEma200PricePoint> Prices, string? Source,
        List<TradingJournal.Services.Screening.DataIssue> Issues);
}
