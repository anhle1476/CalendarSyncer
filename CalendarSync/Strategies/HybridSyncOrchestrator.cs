using CalendarSync.Models;
using CalendarSync.Services;
using Microsoft.Extensions.Options;

namespace CalendarSync.Strategies;

/// <summary>
/// Orchestrates hybrid synchronization using both webhook and polling strategies
/// Manages communication between strategies and handles health status changes
/// </summary>
public class HybridSyncOrchestrator : ISyncOrchestrator, IDisposable
{
    private readonly ILogger<HybridSyncOrchestrator> _logger;
    private readonly WebhookSyncStrategy _webhookStrategy;
    private readonly PollingSyncStrategy _pollingStrategy;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly SyncSettings _syncSettings;

    private bool _isInitialized = false;
    private bool _isRunning = false;
    private bool _isWebhookModeActive = false;
    private int _currentPollingIntervalMinutes;

    public int CurrentPollingIntervalMinutes => _currentPollingIntervalMinutes;
    public bool IsWebhookModeActive => _isWebhookModeActive;

    public event EventHandler<PollingIntervalChangedEventArgs>? PollingIntervalChanged;

    public HybridSyncOrchestrator(
        ILogger<HybridSyncOrchestrator> logger,
        WebhookSyncStrategy webhookStrategy,
        PollingSyncStrategy pollingStrategy,
        IGoogleCalendarService googleCalendarService,
        IOptions<SyncSettings> syncSettings)
    {
        _logger = logger;
        _webhookStrategy = webhookStrategy;
        _pollingStrategy = pollingStrategy;
        _googleCalendarService = googleCalendarService;
        _syncSettings = syncSettings.Value;
        
        // Initialize with fallback polling interval
        _currentPollingIntervalMinutes = _syncSettings.FallbackPollingIntervalMinutes;
        
        // Subscribe to strategy health changes
        _webhookStrategy.HealthChanged += OnWebhookHealthChanged;
        _pollingStrategy.HealthChanged += OnPollingHealthChanged;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("HybridSyncOrchestrator is already initialized");
            return;
        }

        try
        {
            _logger.LogInformation("Initializing hybrid sync orchestrator...");

            // Verify calendar access first
            _logger.LogInformation("Verifying calendar access...");
            await _googleCalendarService.EnsureCalendarExistsAsync(cancellationToken);
            _logger.LogInformation("Successfully verified calendar access");

            // Initialize polling strategy first (always available as fallback)
            var pollingInitialized = await _pollingStrategy.InitializeAsync(cancellationToken);
            if (!pollingInitialized)
            {
                throw new InvalidOperationException("Failed to initialize polling strategy");
            }

            // Try to initialize webhook strategy
            var webhookInitialized = await _webhookStrategy.InitializeAsync(cancellationToken);
            if (webhookInitialized)
            {
                _isWebhookModeActive = true;
                _currentPollingIntervalMinutes = _syncSettings.NormalPollingIntervalMinutes;
                _logger.LogInformation("Successfully initialized hybrid mode with webhook active");
            }
            else
            {
                _isWebhookModeActive = false;
                _currentPollingIntervalMinutes = _syncSettings.FallbackPollingIntervalMinutes;
                _logger.LogWarning("Webhook initialization failed, using polling-only mode");
            }

            // Update polling strategy with current webhook health
            _pollingStrategy.UpdatePollingInterval(_isWebhookModeActive);

            _isInitialized = true;
            OnPollingIntervalChanged(_currentPollingIntervalMinutes);
            
            _logger.LogInformation("Successfully initialized hybrid sync orchestrator with polling interval: {Interval} minutes", 
                _currentPollingIntervalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize hybrid sync orchestrator");
            throw;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("HybridSyncOrchestrator must be initialized before starting");
        }

        if (_isRunning)
        {
            _logger.LogWarning("HybridSyncOrchestrator is already running");
            return;
        }

        try
        {
            _logger.LogInformation("Starting hybrid sync orchestrator...");

            // Always start polling strategy (acts as fallback)
            await _pollingStrategy.StartAsync(cancellationToken);
            _logger.LogInformation("Polling strategy started");

            // Start webhook strategy if it's healthy
            if (_isWebhookModeActive && _webhookStrategy.IsHealthy)
            {
                await _webhookStrategy.StartAsync(cancellationToken);
                _logger.LogInformation("Webhook strategy started");
            }

            _isRunning = true;
            _logger.LogInformation("Hybrid sync orchestrator started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start hybrid sync orchestrator");
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
            _logger.LogInformation("Stopping hybrid sync orchestrator...");

            // Stop both strategies
            var stopTasks = new List<Task>();
            
            if (_webhookStrategy != null)
            {
                stopTasks.Add(_webhookStrategy.StopAsync(cancellationToken));
            }
            
            if (_pollingStrategy != null)
            {
                stopTasks.Add(_pollingStrategy.StopAsync(cancellationToken));
            }

            await Task.WhenAll(stopTasks);

            _isRunning = false;
            _logger.LogInformation("Hybrid sync orchestrator stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping hybrid sync orchestrator");
        }
    }

    private void OnWebhookHealthChanged(object? sender, SyncStrategyHealthChangedEventArgs e)
    {
        var wasWebhookActive = _isWebhookModeActive;
        _isWebhookModeActive = e.IsHealthy;

        // Update polling interval based on webhook health
        var newInterval = e.IsHealthy 
            ? _syncSettings.NormalPollingIntervalMinutes 
            : _syncSettings.FallbackPollingIntervalMinutes;

        if (newInterval != _currentPollingIntervalMinutes)
        {
            var oldInterval = _currentPollingIntervalMinutes;
            _currentPollingIntervalMinutes = newInterval;
            
            // Notify polling strategy about the interval change
            _pollingStrategy.UpdatePollingInterval(e.IsHealthy);
            
            // Notify external listeners
            OnPollingIntervalChanged(_currentPollingIntervalMinutes);

            if (e.IsHealthy && !wasWebhookActive)
            {
                _logger.LogInformation("Webhook strategy recovered, switching to normal polling interval: {NewInterval} minutes (was {OldInterval})", 
                    newInterval, oldInterval);
                
                // Try to start webhook strategy if orchestrator is running
                if (_isRunning)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _webhookStrategy.StartAsync();
                            _logger.LogInformation("Webhook strategy restarted after recovery");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to restart webhook strategy after recovery");
                        }
                    });
                }
            }
            else if (!e.IsHealthy && wasWebhookActive)
            {
                _logger.LogWarning("Webhook strategy became unhealthy, switching to fallback polling interval: {NewInterval} minutes (was {OldInterval})", 
                    newInterval, oldInterval);
            }
        }
    }

    private void OnPollingHealthChanged(object? sender, SyncStrategyHealthChangedEventArgs e)
    {
        if (!e.IsHealthy)
        {
            _logger.LogError("Polling strategy became unhealthy - this is critical as it's our fallback mechanism");
        }
        else
        {
            _logger.LogInformation("Polling strategy health restored");
        }
    }

    private void OnPollingIntervalChanged(int newIntervalMinutes)
    {
        var eventArgs = new PollingIntervalChangedEventArgs
        {
            NewIntervalMinutes = newIntervalMinutes,
            PreviousIntervalMinutes = CurrentPollingIntervalMinutes,
            Reason = _isWebhookModeActive ? "Webhook active - normal interval" : "Webhook inactive - fallback interval",
            Timestamp = DateTime.UtcNow
        };
        
        PollingIntervalChanged?.Invoke(this, eventArgs);
    }

    public void Dispose()
    {
        // Unsubscribe from events
        _webhookStrategy.HealthChanged -= OnWebhookHealthChanged;
        _pollingStrategy.HealthChanged -= OnPollingHealthChanged;

        // Dispose strategies
        _webhookStrategy?.Dispose();
        _pollingStrategy?.Dispose();
    }
}