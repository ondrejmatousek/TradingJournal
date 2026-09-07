namespace TradingJournal.Services.Screening;

public sealed class ScreeningSchedule
{
    public bool Enabled { get; set; } = true;
    public bool External { get; set; }
    public int Hour { get; set; } = 6;
    public int Minute { get; set; }
    public string TimeZone { get; set; } = "Europe/Prague";

    // Calendar days instead of 24-hour intervals keep the local time stable across DST.
    public DateOnly DueDate(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(TimeZone));
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.TimeOfDay >= new TimeSpan(Hour, Minute, 0) ? date : date.AddDays(-1);
    }
}

public sealed record ScreeningUpdateRun(DateOnly ScheduledDate, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, int Processed, int Incomplete, bool MacroUpdated);

public sealed class ScreeningUpdateHostedService(IServiceScopeFactory scopes, ScreeningStore store,
    Microsoft.Extensions.Options.IOptions<ScreeningSchedule> options,
    ILogger<ScreeningUpdateHostedService> logger) : BackgroundService
{
    public const string StateKey = "daily-update-v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try
        {
            // Let startup and the first requests finish before catching up a missed run.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                try { await RunIfDueAsync(DateTimeOffset.UtcNow, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Scheduled screening refresh failed; retrying on the next tick."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task RunIfDueAsync(DateTimeOffset now, CancellationToken ct, bool force = false)
    {
        if (!options.Value.Enabled) return;
        var due = options.Value.DueDate(now);
        var previous = await store.ReadAsync<ScreeningUpdateRun>(StateKey, ct);
        if (!force && previous is { CompletedAt: not null } && previous.ScheduledDate >= due) return;
        var run = new ScreeningUpdateRun(due, now, null, 0, 0, false);
        await store.WriteAsync(StateKey, run, ct);
        using var scope = scopes.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ScreeningDataProvider>();
        var service = scope.ServiceProvider.GetRequiredService<InvestmentScreeningService>();
        try
        {
            await provider.MacroAsync(ct);
            run = run with { MacroUpdated = true };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { logger.LogWarning(ex, "Scheduled macro refresh failed; retaining previous data."); }
        foreach (var stock in InvestmentUniverse.All)
        {
            ct.ThrowIfCancellationRequested();
            var incomplete = true;
            try
            {
                var market = await service.RefreshMarketAsync(stock.Symbol, ct, force);
                incomplete = !string.IsNullOrWhiteSpace(market.Message) || market.Fundamentals == null || market.Prices.Count == 0;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "Scheduled refresh failed for {Symbol}; continuing with remaining stocks.", stock.Symbol); }
            run = run with { Processed = run.Processed + 1, Incomplete = run.Incomplete + (incomplete ? 1 : 0) };
            await store.WriteAsync(StateKey, run, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
        // Completed means the entire universe was attempted, not that every provider returned complete data.
        run = run with { CompletedAt = DateTimeOffset.UtcNow };
        await store.WriteAsync(StateKey, run, ct);
        logger.LogInformation("Daily screening refresh completed: {Count} stocks, {Incomplete} incomplete, macro updated: {Macro}.",
            run.Processed, run.Incomplete, run.MacroUpdated);
    }
}
