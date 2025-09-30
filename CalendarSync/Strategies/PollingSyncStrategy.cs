using CalendarSync.EventProcessing;
using CalendarSync.Models;
using CalendarSync.Services;
using Microsoft.Extensions.Options;

namespace CalendarSync.Strategies;

/// <summary>
/// Handles calendar synchronization via periodic polling of Google Calendar API
/// </summary>
public class PollingSyncStrategy : ICalendarSyncStrategy, IDisposable
{
    private readonly ILogger<PollingSyncStrategy> _logger;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IEventProcessor _eventProcessor;
    private readonly INotificationService _notificationService;
    private readonly GoogleSettings _googleSettings;
    private readonly SyncSettings _syncSettings;

    private bool _isHealthy = true; // Polling is generally always healthy unless there are critical errors
    private bool _isRunning = false;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;
    
    // Polling interval management
    private int _currentPollingIntervalMinutes;
    private bool _isWebhookHealthy = false;

    public string Name => "Polling";
    public bool IsHealthy => _isHealthy;

    public event EventHandler<SyncStrategyHealthChangedEventArgs>? HealthChanged;

    public PollingSyncStrategy(
        ILogger<PollingSyncStrategy> logger,
        IGoogleCalendarService googleCalendarService,
        IEventProcessor eventProcessor,
        INotificationService notificationService,
        IOptions<GoogleSettings> googleSettings,
        IOptions<SyncSettings> syncSettings)
    {
        _logger = logger;
        _googleCalendarService = googleCalendarService;
        _eventProcessor = eventProcessor;
        _notificationService = notificationService;
        _googleSettings = googleSettings.Value;
        _syncSettings = syncSettings.Value;
        
        // Initialize with fallback polling interval (webhook assumed unhealthy initially)
        _currentPollingIntervalMinutes = _syncSettings.FallbackPollingIntervalMinutes;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing polling sync strategy with interval: {Interval} minutes", _currentPollingIntervalMinutes);
            
            // Perform initial full sync if necessary
            await PerformInitialSyncAsync(cancellationToken);
            
            _isHealthy = true;
            OnHealthChanged(true);
            _logger.LogInformation("Successfully initialized polling sync strategy");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize polling sync strategy");
            _isHealthy = false;
            OnHealthChanged(false);
            return false;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Polling sync strategy is already running");
            return;
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _cancellationTokenSource.Token).Token;

            _isRunning = true;
            _pollingTask = RunPollingLoopAsync(combinedToken);
            
            _logger.LogInformation("Started polling sync strategy with interval: {Interval} minutes", _currentPollingIntervalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start polling sync strategy");
            _isHealthy = false;
            OnHealthChanged(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Stopping polling sync strategy...");

            // Cancel ongoing operations
            _cancellationTokenSource?.Cancel();

            // Wait for polling task to complete
            if (_pollingTask != null)
            {
                await _pollingTask;
            }

            _isRunning = false;
            _logger.LogInformation("Polling sync strategy stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping polling sync strategy");
        }
    }

    /// <summary>
    /// Updates the polling interval based on webhook health status
    /// </summary>
    /// <param name="isWebhookHealthy">Whether the webhook is currently healthy</param>
    public void UpdatePollingInterval(bool isWebhookHealthy)
    {
        var wasWebhookHealthy = _isWebhookHealthy;
        _isWebhookHealthy = isWebhookHealthy;

        var newInterval = isWebhookHealthy 
            ? _syncSettings.NormalPollingIntervalMinutes 
            : _syncSettings.FallbackPollingIntervalMinutes;

        if (newInterval != _currentPollingIntervalMinutes)
        {
            var oldInterval = _currentPollingIntervalMinutes;
            _currentPollingIntervalMinutes = newInterval;

            if (isWebhookHealthy && !wasWebhookHealthy)
            {
                _logger.LogInformation("Webhook service recovered, switching to normal polling interval: {NewInterval} minutes (was {OldInterval})", 
                    newInterval, oldInterval);
            }
            else if (!isWebhookHealthy && wasWebhookHealthy)
            {
                _logger.LogWarning("Webhook service became unhealthy, switching to fallback polling interval: {NewInterval} minutes (was {OldInterval})", 
                    newInterval, oldInterval);
            }
        }
    }

    private async Task RunPollingLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Polling sync strategy started at: {Time}", DateTimeOffset.Now);
        _logger.LogInformation("Initial polling interval: {Interval} minutes", _currentPollingIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Polling worker running at: {Time} (interval: {Interval} minutes, webhook healthy: {WebhookHealthy})", 
                    DateTimeOffset.Now, _currentPollingIntervalMinutes, _isWebhookHealthy);

                // Send sync started notification
                await _notificationService.SendSyncStatusNotificationAsync("started", _googleSettings.CalendarId, 0);

                var syncToken = await _eventProcessor.GetLastSyncTokenAsync(_googleSettings.CalendarId);
                var eventsResult = await _googleCalendarService.GetEventsAsync(syncToken, stoppingToken);

                int processedEventCount = 0;

                if (eventsResult?.Items != null)
                {
                    _logger.LogInformation("Successfully retrieved {Count} events from Google Calendar via polling", eventsResult.Items.Count);
                    processedEventCount = eventsResult.Items.Count;
                    
                    if (eventsResult.Items.Count > 0)
                    {
                        await _eventProcessor.ProcessEventsAsync(eventsResult.Items, stoppingToken);
                    }
                }

                // Update sync token if available
                if (!string.IsNullOrEmpty(eventsResult?.NextSyncToken))
                {
                    await _eventProcessor.UpdateSyncTokenAsync(_googleSettings.CalendarId, eventsResult.NextSyncToken);
                    _logger.LogInformation("New sync token saved");
                }

                // Send sync completed notification
                await _notificationService.SendSyncStatusNotificationAsync("completed", _googleSettings.CalendarId, processedEventCount);

                // Use current polling interval (which adjusts based on webhook health)
                await Task.Delay(TimeSpan.FromMinutes(_currentPollingIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                _logger.LogInformation("Polling worker cancellation requested");
                break;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "A Google API error occurred during polling: {Message}", ex.Message);
                
                // Send sync failed notification
                await _notificationService.SendSyncStatusNotificationAsync("failed", _googleSettings.CalendarId, 0);
                
                // Mark as unhealthy temporarily
                var wasHealthy = _isHealthy;
                _isHealthy = false;
                if (wasHealthy)
                {
                    OnHealthChanged(false);
                }
                
                // Exponential backoff or similar retry strategy
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                
                // Restore health status
                _isHealthy = true;
                if (!wasHealthy)
                {
                    OnHealthChanged(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in polling worker execution");
                
                // Send sync failed notification
                await _notificationService.SendSyncStatusNotificationAsync("failed", _googleSettings.CalendarId, 0);
                
                // Mark as unhealthy temporarily
                var wasHealthy = _isHealthy;
                _isHealthy = false;
                if (wasHealthy)
                {
                    OnHealthChanged(false);
                }
                
                // Wait a bit before retrying to avoid rapid failure loops
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                
                // Restore health status
                _isHealthy = true;
                if (!wasHealthy)
                {
                    OnHealthChanged(true);
                }
            }
        }

        _logger.LogInformation("Polling sync strategy stopped at: {Time}", DateTimeOffset.Now);
    }

    private async Task PerformInitialSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Performing initial sync check for calendar {CalendarId}", _googleSettings.CalendarId);

            var syncToken = await _eventProcessor.GetLastSyncTokenAsync(_googleSettings.CalendarId);
            
            // If no sync token exists, this might be the first run - perform a full sync
            if (string.IsNullOrEmpty(syncToken))
            {
                _logger.LogInformation("No sync token found, performing initial full sync");
                
                // Send sync started notification
                await _notificationService.SendSyncStatusNotificationAsync("started", _googleSettings.CalendarId, 0);

                var eventsResult = await _googleCalendarService.GetEventsAsync(null, cancellationToken);
                
                int processedEventCount = 0;
                if (eventsResult?.Items != null)
                {
                    _logger.LogInformation("Processing {Count} events from initial full sync", eventsResult.Items.Count);
                    processedEventCount = eventsResult.Items.Count;
                    
                    if (eventsResult.Items.Count > 0)
                    {
                        await _eventProcessor.ProcessEventsAsync(eventsResult.Items, cancellationToken);
                    }
                }

                // Save the initial sync token
                if (!string.IsNullOrEmpty(eventsResult?.NextSyncToken))
                {
                    await _eventProcessor.UpdateSyncTokenAsync(_googleSettings.CalendarId, eventsResult.NextSyncToken);
                    _logger.LogInformation("Initial sync token saved");
                }

                // Send sync completed notification
                await _notificationService.SendSyncStatusNotificationAsync("completed", _googleSettings.CalendarId, processedEventCount);
            }
            else
            {
                _logger.LogInformation("Sync token exists, skipping initial full sync");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial sync");
            throw;
        }
    }

    private void OnHealthChanged(bool isHealthy)
    {
        var eventArgs = new SyncStrategyHealthChangedEventArgs
        {
            StrategyName = Name,
            IsHealthy = isHealthy,
            Reason = isHealthy ? "Polling strategy recovered" : "Polling strategy failed",
            Timestamp = DateTime.UtcNow
        };
        
        HealthChanged?.Invoke(this, eventArgs);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}