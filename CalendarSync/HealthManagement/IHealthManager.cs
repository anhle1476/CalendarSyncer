namespace CalendarSync.HealthManagement;

/// <summary>
/// Defines the contract for health management
/// </summary>
public interface IHealthManager
{
    /// <summary>
    /// Gets whether the monitored component is healthy
    /// </summary>
    bool IsHealthy { get; }
    
    /// <summary>
    /// Gets the last health check timestamp
    /// </summary>
    DateTime LastHealthCheck { get; }
    
    /// <summary>
    /// Starts health monitoring
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartMonitoringAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Stops health monitoring
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopMonitoringAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Performs a manual health check
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if healthy</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Event raised when health status changes
    /// </summary>
    event EventHandler<HealthChangedEventArgs> HealthChanged;
}

/// <summary>
/// Event arguments for health changes
/// </summary>
public class HealthChangedEventArgs : EventArgs
{
    public bool IsHealthy { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}