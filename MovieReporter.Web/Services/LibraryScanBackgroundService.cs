namespace MovieReporter.Web.Services;

public sealed class LibraryScanBackgroundService : BackgroundService
{
    private readonly LibraryScanCoordinator _scanCoordinator;
    private readonly ILogger<LibraryScanBackgroundService> _logger;

    public LibraryScanBackgroundService(
        LibraryScanCoordinator scanCoordinator,
        ILogger<LibraryScanBackgroundService> logger)
    {
        _scanCoordinator = scanCoordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _scanCoordinator.RefreshAsync(LibraryScanTrigger.InitialAutomatic, stoppingToken);

        var autoScanInterval = _scanCoordinator.GetSnapshot().AutoScanInterval;
        if (autoScanInterval <= TimeSpan.Zero)
        {
            _logger.LogInformation("Automatic periodic library refresh is disabled.");
            return;
        }

        _logger.LogInformation("Automatic periodic library refresh is enabled with an interval of {RefreshInterval}.", autoScanInterval);

        using var timer = new PeriodicTimer(autoScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _scanCoordinator.RefreshAsync(LibraryScanTrigger.Automatic, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Stopped automatic periodic library refresh.");
        }
    }
}
