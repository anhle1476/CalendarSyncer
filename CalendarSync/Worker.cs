namespace CalendarSync;

using CalendarSync.Strategies;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that manages calendar synchronization using the orchestrator pattern
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ISyncOrchestrator _syncOrchestrator;
    private readonly IHostApplicationLifetime _appLifetime;

    public Worker(
        ILogger<Worker> logger,
        ISyncOrchestrator syncOrchestrator,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _syncOrchestrator = syncOrchestrator;
        _appLifetime = appLifetime;
    }

    /// <summary>
    /// Starts the calendar synchronization service
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Calendar sync service starting...");

            // Initialize the orchestrator
            await _syncOrchestrator.InitializeAsync(stoppingToken);

            // Start the synchronization strategies
            await _syncOrchestrator.StartAsync(stoppingToken);

            _logger.LogInformation("Calendar sync service started successfully");

            // Keep the service running until cancellation is requested
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Calendar sync service is stopping due to cancellation request");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in calendar sync service");
            _appLifetime.StopApplication();
        }
    }

    /// <summary>
    /// Stops the calendar synchronization service
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Calendar sync service stopping...");

            // Stop the orchestrator and all strategies
            await _syncOrchestrator.StopAsync(cancellationToken);

            _logger.LogInformation("Calendar sync service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping calendar sync service");
        }
        finally
        {
            await base.StopAsync(cancellationToken);
        }
    }
}
