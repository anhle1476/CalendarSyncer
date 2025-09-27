using CalendarSync.Models;
using Microsoft.Extensions.Options;

namespace CalendarSync
{
    /// <summary>
    /// Main worker service that handles calendar synchronization
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly SyncSettings _syncSettings;

        public Worker(ILogger<Worker> logger, IOptions<SyncSettings> syncSettings)
        {
            _logger = logger;
            _syncSettings = syncSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CalendarSyncService Worker started at: {time}", DateTimeOffset.Now);
            _logger.LogInformation("Sync interval configured to: {interval} minutes", _syncSettings.IntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                    
                    // TODO: Phase 2 - Add Google Calendar API integration
                    // TODO: Phase 3 - Add database synchronization
                    // TODO: Phase 4 - Add UDP notifications
                    
                    await Task.Delay(TimeSpan.FromMinutes(_syncSettings.IntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    _logger.LogInformation("Worker cancellation requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Worker execution");
                    // Wait a bit before retrying to avoid rapid failure loops
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("CalendarSyncService Worker stopped at: {time}", DateTimeOffset.Now);
        }
    }
}
