namespace CalendarSync;

using CalendarSync.Models;
using CalendarSync.Services;
using Microsoft.Extensions.Options;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SyncSettings _syncSettings;
    private readonly IGoogleCalendarService _googleCalendarService;

    public Worker(ILogger<Worker> logger, IOptions<SyncSettings> syncOptions, IGoogleCalendarService googleCalendarService)
    {
        _logger = logger;
        _syncSettings = syncOptions.Value;
        _googleCalendarService = googleCalendarService;
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

                var events = await _googleCalendarService.GetEventsAsync(stoppingToken);
                if (events != null)
                {
                    _logger.LogInformation("Successfully retrieved {count} events from Google Calendar.", events.Count);
                }

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
