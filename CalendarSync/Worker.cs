namespace CalendarSync;

using CalendarSync.Models;
using CalendarSync.Services;
using Microsoft.Extensions.Options;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly SyncSettings _syncSettings;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IDatabaseService _databaseService;
    private readonly INotificationService _notificationService;
    private readonly GoogleSettings _googleSettings;
    private readonly IHostApplicationLifetime _appLifetime;

    public Worker(
        ILogger<Worker> logger, 
        IGoogleCalendarService googleCalendarService, 
        IDatabaseService databaseService,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IOptions<GoogleSettings> googleSettings,
        IOptions<SyncSettings> syncSettings)
    {
        _logger = logger;
        _googleCalendarService = googleCalendarService;
        _databaseService = databaseService;
        _notificationService = notificationService;
        _googleSettings = googleSettings.Value;
        _appLifetime = appLifetime;
        _syncSettings = syncSettings.Value;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Verifying calendar access...");
            await _googleCalendarService.EnsureCalendarExistsAsync(cancellationToken);
            _logger.LogInformation("Successfully verified calendar access.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to verify calendar access. The application will now stop.");
            _appLifetime.StopApplication();
            return;
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CalendarSyncService Worker started at: {time}", DateTimeOffset.Now);
        _logger.LogInformation("Sync interval configured to: {interval} minutes", _syncSettings.IntervalMinutes);

        // Perform initial full sync if necessary
        await PerformInitialSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                // Send sync started notification
                await _notificationService.SendSyncStatusNotificationAsync("started", _googleSettings.CalendarId, 0);

                var syncToken = await _databaseService.GetLastSyncTokenAsync(_googleSettings.CalendarId);
                var eventsResult = await _googleCalendarService.GetEventsAsync(syncToken, stoppingToken);

                int processedEventCount = 0;

                if (eventsResult?.Items != null)
                {
                    _logger.LogInformation("Successfully retrieved {count} events from Google Calendar.", eventsResult.Items.Count);
                    processedEventCount = eventsResult.Items.Count;
                    
                    foreach (var googleEvent in eventsResult.Items)
                    {
                        _logger.LogDebug("Processing event {EventId} with status {Status}", googleEvent.Id, googleEvent.Status);
                        if (googleEvent.Status == "cancelled")
                        {
                            var wasDeleted = await _databaseService.DeleteEventAsync(googleEvent.Id);
                            if (wasDeleted)
                            {
                                _logger.LogInformation("Deleted event {EventId} from local database.", googleEvent.Id);
                                
                                // Send UDP notification for deleted event
                                try
                                {
                                    await _notificationService.SendEventChangeNotificationAsync(googleEvent.Id, "deleted");
                                }
                                catch (System.Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to send UDP notification for event {EventId} deletion", googleEvent.Id);
                                }
                            }
                        }
                        else
                        {
                            var calendarEvent = new CalendarEvent
                            {
                                EventID = googleEvent.Id,
                                CalendarID = _googleSettings.CalendarId,
                                Summary = googleEvent.Summary,
                                Description = googleEvent.Description,
                                Location = googleEvent.Location,
                                StartTime = googleEvent.Start?.DateTimeDateTimeOffset?.DateTime,
                                EndTime = googleEvent.End?.DateTimeDateTimeOffset?.DateTime,
                                CreatedTime = googleEvent.CreatedDateTimeOffset?.DateTime,
                                UpdatedTime = googleEvent.UpdatedDateTimeOffset?.DateTime,
                                Status = googleEvent.Status,
                                OrganizerEmail = googleEvent.Organizer?.Email,
                                Attendees = googleEvent.Attendees != null ? string.Join(",", googleEvent.Attendees.Select(a => a.Email)) : null,
                                Recurrence = googleEvent.Recurrence != null ? string.Join(";", googleEvent.Recurrence) : null
                            };
                            var changeType = await _databaseService.UpsertEventAsync(calendarEvent);
                            _logger.LogInformation("Upserted event {EventId} to local database.", googleEvent.Id);
                            
                            // Send UDP notification for upserted event
                            try
                            {
                                await _notificationService.SendEventChangeNotificationAsync(googleEvent.Id, changeType);
                            }
                            catch (System.Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to send UDP notification for event {EventId} change: {ChangeType}", 
                                    googleEvent.Id, changeType);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("No new events to sync.");
                }

                if (!string.IsNullOrEmpty(eventsResult.NextSyncToken) && eventsResult.NextSyncToken != syncToken)
                {
                    await _databaseService.UpdateLastSyncTokenAsync(_googleSettings.CalendarId, eventsResult.NextSyncToken);
                    _logger.LogInformation("New sync token saved.");
                }

                // Send sync completed notification
                await _notificationService.SendSyncStatusNotificationAsync("completed", _googleSettings.CalendarId, processedEventCount);

                await Task.Delay(TimeSpan.FromMinutes(_syncSettings.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                _logger.LogInformation("Worker cancellation requested");
                break;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "A Google API error occurred: {Message}", ex.Message);
                
                // Send sync failed notification
                await _notificationService.SendSyncStatusNotificationAsync("failed", _googleSettings.CalendarId, 0);
                
                // Exponential backoff or similar retry strategy could be implemented here
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Worker execution");
                
                // Send sync failed notification
                await _notificationService.SendSyncStatusNotificationAsync("failed", _googleSettings.CalendarId, 0);
                
                // Wait a bit before retrying to avoid rapid failure loops
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("CalendarSyncService Worker stopped at: {time}", DateTimeOffset.Now);
    }

    private async Task PerformInitialSyncAsync(CancellationToken cancellationToken)
    {
        var lastSyncToken = await _databaseService.GetLastSyncTokenAsync(_googleSettings.CalendarId);
        if (string.IsNullOrEmpty(lastSyncToken))
        {
            _logger.LogInformation("No sync token found. Performing initial full sync...");

            var allEvents = await _googleCalendarService.GetAllEventsAsync(cancellationToken);

            if (allEvents != null)
            {
                foreach (var googleEvent in allEvents)
                {
                    var calendarEvent = new CalendarEvent
                    {
                        EventID = googleEvent.Id,
                        CalendarID = _googleSettings.CalendarId,
                        Summary = googleEvent.Summary,
                        Description = googleEvent.Description,
                        Location = googleEvent.Location,
                        StartTime = googleEvent.Start?.DateTimeDateTimeOffset?.DateTime,
                        EndTime = googleEvent.End?.DateTimeDateTimeOffset?.DateTime,
                        CreatedTime = googleEvent.CreatedDateTimeOffset?.DateTime,
                        UpdatedTime = googleEvent.UpdatedDateTimeOffset?.DateTime,
                        Status = googleEvent.Status,
                        OrganizerEmail = googleEvent.Organizer?.Email,
                        Attendees = googleEvent.Attendees != null ? string.Join(",", googleEvent.Attendees.Select(a => a.Email)) : null,
                        Recurrence = googleEvent.Recurrence != null ? string.Join(";", googleEvent.Recurrence) : null
                    };

                    var changeType = await _databaseService.UpsertEventAsync(calendarEvent);
                    
                    // Send UDP notification for upserted event
                    try
                    {
                        await _notificationService.SendEventChangeNotificationAsync(googleEvent.Id, changeType);
                    }
                    catch (System.Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send UDP notification for event {EventId} change: {ChangeType}", 
                            googleEvent.Id, changeType);
                    }
                }

                _logger.LogInformation("Initial full sync completed. {count} events synced.", allEvents.Count);

                // After a full sync, the API provides a sync token for future incremental syncs.
                // We need to get it from the response of the last page of events.
                // This part is tricky because GetAllEventsAsync abstracts away the pages.
                // Let's assume for now that the sync token is available after GetAllEventsAsync.
                // We will refine this logic.

                // For now, we'll fetch events again to get a sync token.
                var eventsResult = await _googleCalendarService.GetEventsAsync(null, cancellationToken);
                var newSyncToken = eventsResult.NextSyncToken;

                if (!string.IsNullOrEmpty(newSyncToken))
                {
                    await _databaseService.UpdateLastSyncTokenAsync(_googleSettings.CalendarId, newSyncToken);
                    _logger.LogInformation("New sync token saved.");
                }
            }
        }
        else
        {
            _logger.LogInformation("Existing sync token found. Skipping initial full sync.");
        }
    }
}
