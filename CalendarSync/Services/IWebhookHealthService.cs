namespace CalendarSync.Services
{
    /// <summary>
    /// Interface for monitoring webhook service health and availability
    /// </summary>
    public interface IWebhookHealthService : IDisposable
    {
        /// <summary>
        /// Starts periodic health checks of the webhook service
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for stopping health checks</param>
        Task StartHealthChecksAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Stops periodic health checks
        /// </summary>
        Task StopHealthChecksAsync();

        /// <summary>
        /// Performs a single health check of the webhook service
        /// </summary>
        /// <returns>Health check result with status and details</returns>
        Task<(bool IsHealthy, string Status, DateTime LastCheck, TimeSpan ResponseTime)> CheckHealthAsync();

        /// <summary>
        /// Gets the current health status without performing a new check
        /// </summary>
        bool IsWebhookServiceHealthy { get; }

        /// <summary>
        /// Gets the last health check result
        /// </summary>
        (bool IsHealthy, string Status, DateTime LastCheck, TimeSpan ResponseTime) LastHealthCheck { get; }

        /// <summary>
        /// Event fired when webhook service health status changes
        /// </summary>
        event EventHandler<WebhookHealthChangedEventArgs>? HealthStatusChanged;
    }

    /// <summary>
    /// Event arguments for webhook health status changes
    /// </summary>
    public class WebhookHealthChangedEventArgs : EventArgs
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public bool WasHealthy { get; set; }
    }
}