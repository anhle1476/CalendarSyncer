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

        /// <summary>
        /// Sends an event change notification asynchronously.
        /// </summary>
        /// <param name="eventId">The ID of the event that changed.</param>
        /// <param name="changeType">The type of change (added, updated, deleted).</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task SendEventChangeNotificationAsync(string eventId, string changeType);

        /// <summary>
        /// Sends a sync status notification asynchronously.
        /// </summary>
        /// <param name="status">The sync status (started, completed, failed).</param>
        /// <param name="calendarId">The ID of the calendar being synced.</param>
        /// <param name="eventCount">The number of events processed.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task SendSyncStatusNotificationAsync(string status, string calendarId, int eventCount);
    }
}