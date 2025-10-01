using CalendarDesktop.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CalendarDesktop.Services
{
    /// <summary>
    /// Service for receiving UDP messages from the CalendarSync service
    /// </summary>
    public class UdpListenerService : IDisposable
    {
        private readonly NotificationSettings _notificationSettings;
        private readonly ILogger<UdpListenerService>? _logger;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listeningTask;
        private bool _disposed = false;

        /// <summary>
        /// Event raised when a UDP message is received
        /// </summary>
        public event EventHandler<string>? MessageReceived;

        /// <summary>
        /// Initializes a new instance of the UdpListenerService
        /// </summary>
        /// <param name="notificationSettings">UDP notification settings</param>
        /// <param name="logger">Logger instance (optional)</param>
        public UdpListenerService(
            IOptions<NotificationSettings> notificationSettings,
            ILogger<UdpListenerService>? logger = null)
        {
            _notificationSettings = notificationSettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Starts listening for UDP messages
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        public async Task StartAsync()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UdpListenerService));
            }

            if (_listeningTask != null)
            {
                _logger?.LogWarning("UDP listener is already running");
                return;
            }

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                var endPoint = new IPEndPoint(
                    IPAddress.Parse(_notificationSettings.UdpHost), 
                    _notificationSettings.UdpPort);

                _udpClient = new UdpClient(endPoint);
                
                _logger?.LogInformation("UDP listener started on {Host}:{Port}", 
                    _notificationSettings.UdpHost, _notificationSettings.UdpPort);

                _listeningTask = ListenForMessagesAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start UDP listener");
                throw;
            }
        }

        /// <summary>
        /// Stops listening for UDP messages
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        public async Task StopAsync()
        {
            if (_listeningTask == null)
            {
                return;
            }

            try
            {
                _cancellationTokenSource?.Cancel();
                
                if (_listeningTask != null)
                {
                    await _listeningTask;
                }

                _udpClient?.Close();
                _udpClient?.Dispose();
                _udpClient = null;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _listeningTask = null;

                _logger?.LogInformation("UDP listener stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping UDP listener");
            }
        }

        /// <summary>
        /// Continuously listens for UDP messages
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _udpClient != null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    
                    _logger?.LogDebug("Received UDP message: {Message} from {RemoteEndPoint}", 
                        message, result.RemoteEndPoint);

                    // Raise the MessageReceived event
                    MessageReceived?.Invoke(this, message);
                }
                catch (ObjectDisposedException)
                {
                    // Expected when stopping the service
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // Expected when cancelling
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error receiving UDP message");
                    
                    // Brief delay before retrying to avoid tight loop on persistent errors
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Disposes the UDP listener resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    StopAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error disposing UDP listener service");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }
}