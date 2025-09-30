using CalendarSync.EventProcessing;
using CalendarSync.Models;
using CalendarSync.Services;
using CalendarSync.Utilities;
using Microsoft.Extensions.Options;

namespace CalendarSync.Strategies;

/// <summary>
/// Handles calendar synchronization via Google Calendar webhooks
/// </summary>
public class WebhookSyncStrategy : ICalendarSyncStrategy, IDisposable
{
    private readonly ILogger<WebhookSyncStrategy> _logger;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IRabbitMQService _rabbitMQService;
    private readonly IWebhookHealthService _webhookHealthService;
    private readonly IEventProcessor _eventProcessor;
    private readonly IDebounceService _debounceService;
    private readonly GoogleSettings _googleSettings;
    private readonly WebhookSettings _webhookSettings;
    private readonly SyncSettings _syncSettings;

    // Active webhook channel management
    private string? _activeChannelId = null;
    private string? _activeResourceId = null;
    private readonly object _channelLock = new();
    
    private bool _isHealthy = false;
    private bool _isRunning = false;
    private CancellationTokenSource? _cancellationTokenSource;

    public string Name => "Webhook";
    public bool IsHealthy => _isHealthy;

    public event EventHandler<SyncStrategyHealthChangedEventArgs>? HealthChanged;

    public WebhookSyncStrategy(
        ILogger<WebhookSyncStrategy> logger,
        IGoogleCalendarService googleCalendarService,
        IRabbitMQService rabbitMQService,
        IWebhookHealthService webhookHealthService,
        IEventProcessor eventProcessor,
        IDebounceService debounceService,
        IOptions<GoogleSettings> googleSettings,
        IOptions<WebhookSettings> webhookSettings,
        IOptions<SyncSettings> syncSettings)
    {
        _logger = logger;
        _googleCalendarService = googleCalendarService;
        _rabbitMQService = rabbitMQService;
        _webhookHealthService = webhookHealthService;
        _eventProcessor = eventProcessor;
        _debounceService = debounceService;
        _googleSettings = googleSettings.Value;
        _webhookSettings = webhookSettings.Value;
        _syncSettings = syncSettings.Value;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing webhook sync strategy...");

            // Connect to RabbitMQ
            var rabbitConnected = await _rabbitMQService.ConnectAsync();
            if (!rabbitConnected)
            {
                _logger.LogError("Failed to connect to RabbitMQ");
                return false;
            }

            // Start webhook health monitoring
            await _webhookHealthService.StartHealthChecksAsync(cancellationToken);

            // Subscribe to webhook health changes
            _webhookHealthService.HealthStatusChanged += OnWebhookHealthChanged;

            // Check initial webhook health
            var (isHealthy, status, _, _) = await _webhookHealthService.CheckHealthAsync();
            if (!isHealthy)
            {
                _logger.LogWarning("Webhook service is not initially healthy: {Status}", status);
                _isHealthy = false;
                OnHealthChanged(false);
                return false;
            }

            // Register webhook with Google Calendar
            await RegisterGoogleCalendarWebhookAsync(cancellationToken);

            _isHealthy = true;
            OnHealthChanged(true);
            _logger.LogInformation("Successfully initialized webhook sync strategy");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize webhook sync strategy");
            _isHealthy = false;
            OnHealthChanged(false);
            return false;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Webhook sync strategy is already running");
            return;
        }

        if (!_isHealthy)
        {
            _logger.LogWarning("Cannot start webhook sync strategy - not healthy");
            return;
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _cancellationTokenSource.Token).Token;

            // Start RabbitMQ message consumption
            await _rabbitMQService.StartConsumingAsync(OnCalendarEventReceived, combinedToken);
            
            _isRunning = true;
            _logger.LogInformation("Started webhook sync strategy - consuming RabbitMQ messages");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start webhook sync strategy");
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
            _logger.LogInformation("Stopping webhook sync strategy...");

            // Cancel ongoing operations
            _cancellationTokenSource?.Cancel();

            // Stop the active webhook channel
            await StopExistingWebhookChannelAsync(cancellationToken);

            // Unsubscribe from health status changes
            _webhookHealthService.HealthStatusChanged -= OnWebhookHealthChanged;

            _isRunning = false;
            _logger.LogInformation("Webhook sync strategy stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping webhook sync strategy");
        }
    }

    private void OnWebhookHealthChanged(object? sender, WebhookHealthChangedEventArgs e)
    {
        var wasHealthy = _isHealthy;
        _isHealthy = e.IsHealthy;

        if (e.IsHealthy && !wasHealthy)
        {
            _logger.LogInformation("Webhook service recovered");
            OnHealthChanged(true);
        }
        else if (!e.IsHealthy && wasHealthy)
        {
            _logger.LogWarning("Webhook service became unhealthy: {Status}", e.Status);
            OnHealthChanged(false);
        }
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
            await _debounceService.DebounceAsync(
                webhookNotification.CalendarId,
                webhookNotification.CalendarId,
                async (calendarId, cancellationToken) => await PerformIncrementalSyncAsync(calendarId),
                TimeSpan.FromMilliseconds(_webhookSettings.DebounceDelayMs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook notification: CalendarId={CalendarId}, EventType={EventType}, ChannelId={ChannelId}", 
                webhookNotification.CalendarId, webhookNotification.EventType, webhookNotification.ChannelId);
        }
    }

    private async Task PerformIncrementalSyncAsync(string calendarId)
    {
        try
        {
            _logger.LogInformation("Performing incremental sync for calendar {CalendarId} triggered by webhook", calendarId);

            var syncToken = await _eventProcessor.GetLastSyncTokenAsync(calendarId);
            var eventsResult = await _googleCalendarService.GetEventsAsync(syncToken, CancellationToken.None);

            if (eventsResult?.Items != null && eventsResult.Items.Count > 0)
            {
                _logger.LogInformation("Processing {Count} events from webhook-triggered incremental sync", eventsResult.Items.Count);
                await _eventProcessor.ProcessEventsAsync(eventsResult.Items, CancellationToken.None);
            }

            // Update sync token if available
            if (!string.IsNullOrEmpty(eventsResult?.NextSyncToken))
            {
                await _eventProcessor.UpdateSyncTokenAsync(calendarId, eventsResult.NextSyncToken);
            }

            _logger.LogInformation("Completed incremental sync for calendar {CalendarId}", calendarId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during incremental sync for calendar {CalendarId}", calendarId);
        }
    }

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

    private void OnHealthChanged(bool isHealthy)
    {
        var eventArgs = new SyncStrategyHealthChangedEventArgs
        {
            StrategyName = Name,
            IsHealthy = isHealthy,
            Reason = isHealthy ? "Webhook strategy recovered" : "Webhook strategy failed",
            Timestamp = DateTime.UtcNow
        };
        
        HealthChanged?.Invoke(this, eventArgs);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        
        // Cancel all pending debounce operations
        _debounceService.CancelAllDebounces();
    }
}