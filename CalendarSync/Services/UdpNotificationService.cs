using CalendarSync.Models;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    /// <summary>
    /// A service that sends notifications over UDP.
    /// </summary>
    public class UdpNotificationService : INotificationService
    {
        private readonly NotificationSettings _notificationSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpNotificationService"/> class.
        /// </summary>
        /// <param name="notificationSettings">The notification settings.</param>
        public UdpNotificationService(IOptions<NotificationSettings> notificationSettings)
        {
            _notificationSettings = notificationSettings.Value;
        }

        /// <summary>
        /// Sends a notification message asynchronously over UDP.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task SendNotificationAsync(string message)
        {
            using var udpClient = new UdpClient();
            var data = Encoding.UTF8.GetBytes(message);
            await udpClient.SendAsync(data, data.Length, _notificationSettings.UdpHost, _notificationSettings.UdpPort);
        }
    }
}