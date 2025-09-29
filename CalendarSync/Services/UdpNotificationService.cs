using CalendarSync.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    /// <summary>
    /// Service for sending notifications via UDP protocol.
    /// </summary>
    public class UdpNotificationService : INotificationService, IDisposable
    {
        private readonly NotificationSettings _notificationSettings;
        private readonly ILogger<UdpNotificationService> _logger;
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _endPoint;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpNotificationService"/> class.
        /// </summary>
        /// <param name="notificationSettings">The notification settings.</param>
        /// <param name="logger">The logger instance.</param>
        public UdpNotificationService(
            IOptions<NotificationSettings> notificationSettings,
            ILogger<UdpNotificationService> logger)
        {
            _notificationSettings = notificationSettings.Value;
            _logger = logger;
            
            try
            {
                _udpClient = new UdpClient();
                _endPoint = new IPEndPoint(
                    IPAddress.Parse(_notificationSettings.UdpHost), 
                    _notificationSettings.UdpPort);
                
                _logger.LogInformation("UDP notification service initialized. Target: {Host}:{Port}", 
                    _notificationSettings.UdpHost, _notificationSettings.UdpPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize UDP notification service");
                throw;
            }
        }

        /// <summary>
        /// Sends a notification message via UDP.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task SendNotificationAsync(string message)
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to send notification on disposed UDP service");
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                _logger.LogWarning("Attempted to send empty or null message");
                return;
            }

            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                
                _logger.LogDebug("Sending UDP notification: {Message} to {Host}:{Port}", 
                    message, _notificationSettings.UdpHost, _notificationSettings.UdpPort);

                await _udpClient.SendAsync(messageBytes, messageBytes.Length, _endPoint);
                
                _logger.LogInformation("UDP notification sent successfully: {Message}", message);
            }
            catch (SocketException ex)
            {
                _logger.LogError(ex, "Network error while sending UDP notification: {Message}. Error: {ErrorCode}", 
                    message, ex.ErrorCode);
                
                // Don't rethrow to avoid breaking the sync process
                // The notification is not critical for the main functionality
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError(ex, "UDP client was disposed while sending notification: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while sending UDP notification: {Message}", message);
            }
        }

        /// <summary>
        /// Sends a notification for a calendar event change.
        /// </summary>
        /// <param name="eventId">The ID of the changed event.</param>
        /// <param name="changeType">The type of change (added, updated, deleted).</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task SendEventChangeNotificationAsync(string eventId, string changeType)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                _logger.LogWarning("Attempted to send event notification with empty event ID");
                return;
            }

            // Create a structured message for event changes
            var notificationMessage = $"EVENT_CHANGE|{changeType}|{eventId}|{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}";
            
            await SendNotificationAsync(notificationMessage);
        }

        /// <summary>
        /// Sends a notification for sync status changes.
        /// </summary>
        /// <param name="status">The sync status (started, completed, failed).</param>
        /// <param name="calendarId">The ID of the calendar being synced.</param>
        /// <param name="eventCount">Number of events processed.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task SendSyncStatusNotificationAsync(string status, string calendarId, int eventCount)
        {
            var notificationMessage = $"SYNC_STATUS|{status}|{calendarId}|{eventCount}|{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}";
            
            await SendNotificationAsync(notificationMessage);
        }

        /// <summary>
        /// Disposes the UDP client resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                    _logger.LogInformation("UDP notification service disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing UDP notification service");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }
}