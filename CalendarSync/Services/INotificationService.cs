using System.Threading.Tasks;

namespace CalendarSync.Services
{
    /// <summary>
    /// Defines the contract for a service that sends notifications.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Sends a notification message asynchronously.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task SendNotificationAsync(string message);
    }
}