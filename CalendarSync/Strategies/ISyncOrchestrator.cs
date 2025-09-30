namespace CalendarSync.Strategies;

/// <summary>
/// Defines the contract for orchestrating multiple sync strategies
/// </summary>
public interface ISyncOrchestrator
{
    /// <summary>
    /// Gets the current polling interval in minutes
    /// </summary>
    int CurrentPollingIntervalMinutes { get; }
    
    /// <summary>
    /// Gets whether webhook mode is currently active and healthy
    /// </summary>
    bool IsWebhookModeActive { get; }
    
    /// <summary>
    /// Initializes all strategies
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Starts orchestration of all strategies
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Stops orchestration of all strategies
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Event raised when the polling interval should change
    /// </summary>
    event EventHandler<PollingIntervalChangedEventArgs> PollingIntervalChanged;
}

/// <summary>
/// Event arguments for polling interval changes
/// </summary>
public class PollingIntervalChangedEventArgs : EventArgs
{
    public int NewIntervalMinutes { get; set; }
    public int PreviousIntervalMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}