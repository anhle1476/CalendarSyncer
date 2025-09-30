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
    private readonly IRabbitMQService _rabbitMQService;
    private readonly IWebhookHealthService _webhookHealthService;
    private readonly GoogleSettings _googleSettings;
    private readonly WebhookSettings _webhookSettings;
    private readonly IHostApplicationLifetime _appLifetime;

    private bool _isWebhookHealthy = false;
    private int _currentPollingIntervalMinutes;
    
    // Debounce mechanism for webhook events
    private readonly Dictionary<string, Timer> _debounceTimers = new();
    private readonly object _debounceTimersLock = new();
    
    // Active webhook channel management
    private string? _activeChannelId = null;
    private string? _activeResourceId = null;
    private readonly object _channelLock = new();

    public Worker(
        ILogger<Worker> logger, 
        IGoogleCalendarService googleCalendarService, 
        IDatabaseService databaseService,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IRabbitMQService rabbitMQService,
        IWebhookHealthService webhookHealthService,
        IOptions<GoogleSettings> googleSettings,
        IOptions<SyncSettings> syncSettings,
        IOptions<WebhookSettings> webhookSettings)
    {
        _logger = logger;
        _googleCalendarService = googleCalendarService;
        _databaseService = databaseService;
        _notificationService = notificationService;
        _rabbitMQService = rabbitMQService;
        _webhookHealthService = webhookHealthService;
        _googleSettings = googleSettings.Value;
        _webhookSettings = webhookSettings.Value;
        _appLifetime = appLifetime;
        _syncSettings = syncSettings.Value;
        
        // Initialize with fallback polling interval
        _currentPollingIntervalMinutes = _syncSettings.FallbackPollingIntervalMinutes;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Verifying calendar access...");
            await _googleCalendarService.EnsureCalendarExistsAsync(cancellationToken);
            _logger.LogInformation("Successfully verified calendar access.");

            // Always initialize hybrid mode
            await InitializeHybridModeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to verify calendar access. The application will now stop.");
            _appLifetime.StopApplication();
            return;
        }

        await base.StartAsync(cancellationToken);
    }

    private async Task InitializeHybridModeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing hybrid mode (webhook + polling backup)");

        try
        {
            // Try to initialize webhook mode first
            await InitializeWebhookModeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize webhook component, using polling only");
            _isWebhookHealthy = false;
        }
        
        // Subscribe to webhook health changes for automatic fallback
        _webhookHealthService.HealthStatusChanged += OnWebhookHealthChanged;
        
        _logger.LogInformation("Successfully initialized hybrid mode with polling interval: {Interval} minutes", 
            _currentPollingIntervalMinutes);
    }

    private async Task InitializeWebhookModeAsync(CancellationToken cancellationToken)
    {
        // Connect to RabbitMQ
        var rabbitConnected = await _rabbitMQService.ConnectAsync();
        if (!rabbitConnected)
        {
            throw new InvalidOperationException("Failed to connect to RabbitMQ");
        }

        // Start webhook health monitoring
        await _webhookHealthService.StartHealthChecksAsync(cancellationToken);

        // Check initial webhook health
        var (isHealthy, status, _, _) = await _webhookHealthService.CheckHealthAsync();
        if (!isHealthy)
        {
            throw new InvalidOperationException($"Webhook service is not healthy: {status}");
        }

        // Register webhook with Google Calendar
        await RegisterGoogleCalendarWebhookAsync(cancellationToken);

        _isWebhookHealthy = true;
        _currentPollingIntervalMinutes = _syncSettings.NormalPollingIntervalMinutes;
        _logger.LogInformation("Successfully initialized webhook mode");
    }

    private void OnWebhookHealthChanged(object? sender, WebhookHealthChangedEventArgs e)
    {
        if (e.IsHealthy && !e.WasHealthy)
        {
            _logger.LogInformation("Webhook service recovered, switching to normal polling interval: {Interval} minutes", 
                _syncSettings.NormalPollingIntervalMinutes);
            _isWebhookHealthy = true;
            _currentPollingIntervalMinutes = _syncSettings.NormalPollingIntervalMinutes;
        }
        else if (!e.IsHealthy && e.WasHealthy)
        {
            _logger.LogWarning("Webhook service became unhealthy: {Status}, switching to fallback polling interval: {Interval} minutes", 
                e.Status, _syncSettings.FallbackPollingIntervalMinutes);
            _isWebhookHealthy = false;
            _currentPollingIntervalMinutes = _syncSettings.FallbackPollingIntervalMinutes;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Calendar sync worker started in hybrid mode.");

        // Start RabbitMQ message consumption if webhook is healthy
        if (_isWebhookHealthy)
        {
            await _rabbitMQService.StartConsumingAsync(OnCalendarEventReceived, stoppingToken);
            _logger.LogInformation("Started consuming RabbitMQ messages for webhook events");
        }

        // Always run polling loop as backup
        await RunPollingLoopAsync(stoppingToken);
    }

    private async Task RunPollingLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CalendarSyncService Worker started at: {time}", DateTimeOffset.Now);
        _logger.LogInformation("Initial polling interval: {interval} minutes", _currentPollingIntervalMinutes);

        // Perform initial full sync if necessary
        await PerformInitialSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Worker running at: {time} (polling interval: {interval} minutes, webhook healthy: {webhookHealthy})", 
                    DateTimeOffset.Now, _currentPollingIntervalMinutes, _isWebhookHealthy);

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

                // Use current polling interval (which adjusts based on webhook health)
                await Task.Delay(TimeSpan.FromMinutes(_currentPollingIntervalMinutes), stoppingToken);
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

    private async Task OnCalendarEventReceived(WebhookNotification webhookNotification)
    {
        try
        {
            _logger.LogInformation("Received webhook notification: CalendarId={CalendarId}, EventType={EventType}, ResourceId={ResourceId}, ChannelId={ChannelId}", 
                webhookNotification.CalendarId, webhookNotification.EventType, webhookNotification.ResourceId, webhookNotification.ChannelId);

            // Validate that this notification is from the active channel
            string? activeChannelId = null;
            lock (_channelLock)
            {
                activeChannelId = _activeChannelId;
            }

            if (string.IsNullOrEmpty(activeChannelId))
            {
                _logger.LogWarning("No active channel ID stored. Ignoring webhook notification from channel {ChannelId}", webhookNotification.ChannelId);
                return;
            }

            if (!string.Equals(webhookNotification.ChannelId, activeChannelId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Ignoring webhook notification from inactive channel {ChannelId}. Active channel is {ActiveChannelId}", 
                    webhookNotification.ChannelId, activeChannelId);
                return;
            }

            _logger.LogInformation("Processing webhook notification from active channel {ChannelId}", webhookNotification.ChannelId);

            // Use debounce mechanism to prevent redundant API calls
            await DebounceIncrementalSync(webhookNotification.CalendarId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook notification: CalendarId={CalendarId}, EventType={EventType}, ChannelId={ChannelId}", 
                webhookNotification.CalendarId, webhookNotification.EventType, webhookNotification.ChannelId);
        }
    }

    /// <summary>
    /// Implements debounce mechanism for incremental sync to prevent redundant API calls
    /// when multiple webhook events are received rapidly
    /// </summary>
    /// <param name="calendarId">The calendar ID to sync</param>
    private async Task DebounceIncrementalSync(string calendarId)
    {
        await Task.Run(() =>
        {
            lock (_debounceTimersLock)
            {
                // Cancel existing timer for this calendar if it exists
                if (_debounceTimers.TryGetValue(calendarId, out var existingTimer))
                {
                    existingTimer.Dispose();
                    _logger.LogDebug("Cancelled existing debounce timer for calendar {CalendarId}", calendarId);
                }

                // Create new timer that will trigger sync after debounce delay
                var timer = new Timer(async _ =>
                {
                    try
                    {
                        _logger.LogInformation("Debounce timer expired for calendar {CalendarId}. Triggering incremental sync.", calendarId);
                        await TriggerIncrementalSyncAsync(calendarId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in debounced incremental sync for calendar {CalendarId}", calendarId);
                    }
                    finally
                    {
                        // Clean up timer after execution
                        lock (_debounceTimersLock)
                        {
                            if (_debounceTimers.TryGetValue(calendarId, out var timerToRemove))
                            {
                                timerToRemove.Dispose();
                                _debounceTimers.Remove(calendarId);
                            }
                        }
                    }
                }, null, _webhookSettings.DebounceDelayMs, Timeout.Infinite);

                _debounceTimers[calendarId] = timer;
                _logger.LogDebug("Started debounce timer for calendar {CalendarId} with delay {DelayMs}ms", 
                    calendarId, _webhookSettings.DebounceDelayMs);
            }
        });
    }

    /// <summary>
    /// Triggers an incremental sync for the specified calendar using stored sync token
    /// </summary>
    /// <param name="calendarId">The calendar ID to sync</param>
    private async Task TriggerIncrementalSyncAsync(string calendarId)
    {
        try
        {
            _logger.LogInformation("Starting incremental sync for calendar {CalendarId}", calendarId);

            // Get the last sync token from database
            var syncToken = await _databaseService.GetLastSyncTokenAsync(calendarId);
            
            if (string.IsNullOrEmpty(syncToken))
            {
                _logger.LogWarning("No sync token found for calendar {CalendarId}. Performing full sync instead.", calendarId);
                await PerformInitialSyncAsync(CancellationToken.None);
                return;
            }

            // Perform incremental sync using the stored sync token
            var eventsResult = await _googleCalendarService.GetEventsAsync(syncToken, CancellationToken.None);
            
            if (eventsResult?.Items != null)
            {
                _logger.LogInformation("Retrieved {EventCount} events from incremental sync for calendar {CalendarId}", 
                    eventsResult.Items.Count, calendarId);

                // Process each event from the incremental sync
                foreach (var googleEvent in eventsResult.Items)
                {
                    var calendarEvent = new CalendarEvent
                    {
                        EventID = googleEvent.Id,
                        CalendarID = calendarId,
                        Summary = googleEvent.Summary,
                        Description = googleEvent.Description,
                        StartTime = googleEvent.Start?.DateTime ?? DateTime.Parse(googleEvent.Start?.Date ?? DateTime.Now.ToString()),
                        EndTime = googleEvent.End?.DateTime ?? DateTime.Parse(googleEvent.End?.Date ?? DateTime.Now.ToString()),
                        CreatedTime = googleEvent.Created ?? DateTime.Now,
                        UpdatedTime = googleEvent.Updated ?? DateTime.Now,
                        Location = googleEvent.Location,
                        Status = googleEvent.Status,
                        OrganizerEmail = googleEvent.Organizer?.Email,
                        Attendees = googleEvent.Attendees != null ? string.Join(", ", googleEvent.Attendees.Select(a => a.Email)) : null,
                        Recurrence = googleEvent.Recurrence != null ? string.Join(", ", googleEvent.Recurrence) : null
                    };

                    // Handle deleted events
                    if (googleEvent.Status == "cancelled")
                    {
                        var deleted = await _databaseService.DeleteEventAsync(googleEvent.Id);
                        if (deleted)
                        {
                            _logger.LogInformation("Deleted event: {EventId}", googleEvent.Id);
                            await _notificationService.SendEventChangeNotificationAsync(googleEvent.Id, "deleted");
                        }
                    }
                    else
                    {
                        // Upsert the event
                        var changeType = await _databaseService.UpsertEventAsync(calendarEvent);
                        _logger.LogInformation("Upserted event: {EventId} (Change: {ChangeType})", googleEvent.Id, changeType);
                        await _notificationService.SendEventChangeNotificationAsync(googleEvent.Id, changeType);
                    }
                }

                // Update sync token if provided
                if (!string.IsNullOrEmpty(eventsResult.NextSyncToken))
                {
                    await _databaseService.UpdateLastSyncTokenAsync(calendarId, eventsResult.NextSyncToken);
                    _logger.LogInformation("Updated sync token for calendar {CalendarId}", calendarId);
                }
            }
            else
            {
                _logger.LogInformation("No events returned from incremental sync for calendar {CalendarId}", calendarId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during incremental sync for calendar {CalendarId}", calendarId);
        }
    }

    /// <summary>
    /// Performs initial sync - uses existing sync token if available, otherwise performs full sync
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task PerformInitialSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if we have an existing sync token
            var existingSyncToken = await _databaseService.GetLastSyncTokenAsync(_googleSettings.CalendarId);
            
            if (!string.IsNullOrEmpty(existingSyncToken))
            {
                _logger.LogInformation("Fetching events with sync token: {SyncToken}", existingSyncToken);
                _logger.LogInformation("Performing incremental sync for calendar {CalendarId} using existing sync token", _googleSettings.CalendarId);
            }
            else
            {
                _logger.LogInformation("Fetching events with sync token: null");
                _logger.LogInformation("No existing sync token found. Performing initial full sync for calendar {CalendarId}", _googleSettings.CalendarId);
            }

            // Use existing sync token if available, otherwise null for full sync
            var eventsResult = await _googleCalendarService.GetEventsAsync(existingSyncToken, cancellationToken);
            
            if (eventsResult?.Items != null)
            {
                var syncType = string.IsNullOrEmpty(existingSyncToken) ? "initial" : "incremental";
                _logger.LogInformation("Retrieved {EventCount} events from {SyncType} sync", eventsResult.Items.Count, syncType);
                
                // Convert Google events to CalendarEvent objects
                var calendarEvents = eventsResult.Items.Select(googleEvent => new CalendarEvent
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
                }).ToList();
                
                // Batch upsert all events
                var upsertResults = await _databaseService.UpsertEventsBatchAsync(calendarEvents);
                
                // Log batch operation results
                var addedCount = upsertResults.Values.Count(v => v == "added");
                var updatedCount = upsertResults.Values.Count(v => v == "updated");
                _logger.LogInformation("Batch upsert completed: {AddedCount} added, {UpdatedCount} updated", addedCount, updatedCount);
                
                // Save the sync token for future incremental syncs
                if (!string.IsNullOrEmpty(eventsResult.NextSyncToken))
                {
                    await _databaseService.UpdateLastSyncTokenAsync(_googleSettings.CalendarId, eventsResult.NextSyncToken);
                    var tokenAction = string.IsNullOrEmpty(existingSyncToken) ? "Saved initial" : "Updated";
                    _logger.LogInformation("{TokenAction} sync token for future incremental syncs", tokenAction);
                }
            }
            
            var completionMessage = string.IsNullOrEmpty(existingSyncToken) ? "Initial full sync completed successfully" : "Initial incremental sync completed successfully";
            _logger.LogInformation(completionMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial full sync");
            throw;
        }
    }

    /// <summary>
    /// Registers webhook with Google Calendar API for real-time notifications
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task RegisterGoogleCalendarWebhookAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Stop any existing webhook channel first
            await StopExistingWebhookChannelAsync(cancellationToken);
            
            _logger.LogInformation("Registering Google Calendar webhook for calendar {CalendarId}", _googleSettings.CalendarId);
            
            // Use the correct webhook URL from WebhookSettings
            var channel = await _googleCalendarService.RegisterWebhookAsync(_webhookSettings.ServiceUrl, cancellationToken);
            
            // Store the active channel information
            lock (_channelLock)
            {
                _activeChannelId = channel.Id;
                _activeResourceId = channel.ResourceId;
            }
            
            _logger.LogInformation("Successfully registered Google Calendar webhook. Channel ID: {ChannelId}, Resource ID: {ResourceId}", 
                channel.Id, channel.ResourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Google Calendar webhook");
            throw;
        }
    }

    /// <summary>
    /// Stops the existing webhook channel if one is active
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task StopExistingWebhookChannelAsync(CancellationToken cancellationToken)
    {
        string? channelId = null;
        string? resourceId = null;
        
        lock (_channelLock)
        {
            channelId = _activeChannelId;
            resourceId = _activeResourceId;
        }
        
        if (!string.IsNullOrEmpty(channelId) && !string.IsNullOrEmpty(resourceId))
        {
            try
            {
                _logger.LogInformation("Stopping existing webhook channel {ChannelId}", channelId);
                await _googleCalendarService.StopWebhookAsync(channelId, resourceId, cancellationToken);
                
                lock (_channelLock)
                {
                    _activeChannelId = null;
                    _activeResourceId = null;
                }
                
                _logger.LogInformation("Successfully stopped existing webhook channel {ChannelId}", channelId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop existing webhook channel {ChannelId}. This may be expected if the channel was already expired.", channelId);
                
                // Clear the stored channel info even if stopping failed
                lock (_channelLock)
                {
                    _activeChannelId = null;
                    _activeResourceId = null;
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Calendar Sync Worker...");
        
        // Stop the active webhook channel
        await StopExistingWebhookChannelAsync(cancellationToken);
        
        // Clean up debounce timers
        lock (_debounceTimersLock)
        {
            foreach (var timer in _debounceTimers.Values)
            {
                timer.Dispose();
            }
            _debounceTimers.Clear();
        }
        
        // Unsubscribe from health status changes
        _webhookHealthService.HealthStatusChanged -= OnWebhookHealthChanged;
        
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Calendar Sync Worker stopped.");
    }
}
