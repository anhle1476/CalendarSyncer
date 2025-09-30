using CalendarSync.Models;

namespace CalendarSync.Strategies;

/// <summary>
/// Defines the contract for calendar synchronization strategies
/// </summary>
public interface ICalendarSyncStrategy
{
    /// <summary>
    /// Gets the name of the strategy
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Gets whether the strategy is currently healthy
    /// </summary>
    bool IsHealthy { get; }
    
    /// <summary>
    /// Initializes the strategy
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if initialization was successful</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Starts the strategy execution
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Stops the strategy execution
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Event raised when the strategy health changes
    /// </summary>
    event EventHandler<SyncStrategyHealthChangedEventArgs> HealthChanged;
}

/// <summary>
/// Event arguments for strategy health changes
/// </summary>
public class SyncStrategyHealthChangedEventArgs : EventArgs
{
    public string StrategyName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}