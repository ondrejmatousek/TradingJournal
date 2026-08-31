namespace TradingJournal.Services;

public sealed class SeasonalityUpdateHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SeasonalityUpdateState _state;
    private readonly ILogger<SeasonalityUpdateHostedService> _logger;

    public SeasonalityUpdateHostedService(IServiceScopeFactory scopeFactory, SeasonalityUpdateState state, ILogger<SeasonalityUpdateHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _state.Started();
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<SeasonalityService>().UpdateAsync(stoppingToken);
                    _state.Completed();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _state.Failed(exception);
                    _logger.LogError(exception, "Scheduled seasonality update failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
