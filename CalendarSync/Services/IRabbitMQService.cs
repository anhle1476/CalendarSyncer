using CalendarSync.Models;

namespace CalendarSync.Services
{
    /// <summary>
    /// Interface for RabbitMQ message queue operations
    /// </summary>
    public interface IRabbitMQService : IDisposable
    {
        /// <summary>
        /// Establishes connection to RabbitMQ server
        /// </summary>
        /// <returns>True if connection successful, false otherwise</returns>
        Task<bool> ConnectAsync();

        /// <summary>
        /// Disconnects from RabbitMQ server
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Checks if the service is connected to RabbitMQ
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Starts consuming messages from the calendar events queue
        /// </summary>
        /// <param name="messageHandler">Handler function for processing received messages</param>
        /// <param name="cancellationToken">Cancellation token for stopping consumption</param>
        Task StartConsumingAsync(Func<WebhookNotification, Task> messageHandler, CancellationToken cancellationToken);

        /// <summary>
        /// Stops consuming messages from the queue
        /// </summary>
        Task StopConsumingAsync();

        /// <summary>
        /// Publishes a test message to verify RabbitMQ connectivity
        /// </summary>
        /// <returns>True if message was published successfully</returns>
        Task<bool> PublishTestMessageAsync();

        /// <summary>
        /// Gets connection health information
        /// </summary>
        /// <returns>Health status information</returns>
        Task<(bool IsHealthy, string Status, DateTime LastCheck)> GetHealthAsync();
    }
}