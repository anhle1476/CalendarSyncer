using CalendarSync.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace CalendarSync.Services
{
    /// <summary>
    /// RabbitMQ service implementation for message queue operations
    /// </summary>
    public class RabbitMQService : IRabbitMQService
    {
        private readonly ILogger<RabbitMQService> _logger;
        private readonly RabbitMQSettings _settings;
        private IConnection? _connection;
        private IModel? _channel;
        private string? _consumerTag;
        private bool _disposed = false;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private bool _lastHealthStatus = false;
        private string _lastHealthMessage = "Not checked";

        public RabbitMQService(ILogger<RabbitMQService> logger, IOptions<RabbitMQSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
        }

        public bool IsConnected => _connection?.IsOpen == true && _channel?.IsOpen == true;

        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (IsConnected)
                {
                    _logger.LogInformation("Already connected to RabbitMQ");
                    return true;
                }

                _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port}", _settings.Host, _settings.Port);

                var factory = new ConnectionFactory
                {
                    HostName = _settings.Host,
                    Port = _settings.Port,
                    UserName = _settings.Username,
                    Password = _settings.Password,
                    VirtualHost = _settings.VirtualHost,
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(_settings.ConnectionTimeoutSeconds),
                    RequestedHeartbeat = TimeSpan.FromSeconds(_settings.HeartbeatSeconds),
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
                };

                _connection = factory.CreateConnection("CalendarSyncService");
                _channel = _connection.CreateModel();

                // Only declare exchange, assume queue is already declared by webhook service
                _channel.ExchangeDeclare(
                    exchange: _settings.ExchangeName,
                    type: ExchangeType.Direct,
                    durable: _settings.Durable);

                // Check if queue exists (passive declare)
                try
                {
                    _channel.QueueDeclarePassive(_settings.QueueName);
                    _logger.LogInformation("Queue {QueueName} exists and is accessible", _settings.QueueName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queue {QueueName} does not exist or is not accessible. Make sure webhook service is running first.", _settings.QueueName);
                    throw new InvalidOperationException($"Queue {_settings.QueueName} must be created by webhook service first");
                }

                // Bind to existing queue
                _channel.QueueBind(
                    queue: _settings.QueueName,
                    exchange: _settings.ExchangeName,
                    routingKey: _settings.RoutingKey);

                _logger.LogInformation("Successfully connected to RabbitMQ");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to RabbitMQ");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                await StopConsumingAsync();

                _channel?.Close();
                _channel?.Dispose();
                _channel = null;

                _connection?.Close();
                _connection?.Dispose();
                _connection = null;

                _logger.LogInformation("Disconnected from RabbitMQ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during RabbitMQ disconnection");
            }
        }

        public async Task StartConsumingAsync(Func<WebhookNotification, Task> messageHandler, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to RabbitMQ");
            }

            if (_consumerTag != null)
            {
                _logger.LogWarning("Already consuming messages");
                return;
            }

            try
            {
                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        
                        _logger.LogDebug("Received message: {Message}", message);

                        var webhookNotification = JsonSerializer.Deserialize<WebhookNotification>(message);
                        if (webhookNotification != null)
                        {
                            await messageHandler(webhookNotification);
                            _channel.BasicAck(ea.DeliveryTag, false);
                            _logger.LogDebug("Message processed successfully");
                        }
                        else
                        {
                            _logger.LogWarning("Failed to deserialize message: {Message}", message);
                            _channel.BasicNack(ea.DeliveryTag, false, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message");
                        _channel.BasicNack(ea.DeliveryTag, false, true);
                    }
                };

                _consumerTag = _channel.BasicConsume(
                    queue: _settings.QueueName,
                    autoAck: false,
                    consumer: consumer);

                _logger.LogInformation("Started consuming messages from queue: {QueueName}", _settings.QueueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start consuming messages");
                throw;
            }
        }

        public async Task StopConsumingAsync()
        {
            if (_consumerTag != null && _channel?.IsOpen == true)
            {
                try
                {
                    _channel.BasicCancel(_consumerTag);
                    _consumerTag = null;
                    _logger.LogInformation("Stopped consuming messages");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping message consumption");
                }
            }
        }

        public async Task<bool> PublishTestMessageAsync()
        {
            if (!IsConnected || _channel == null)
            {
                _logger.LogWarning("Cannot publish test message - not connected to RabbitMQ");
                return false;
            }

            try
            {
                // Create a test webhook notification instead of a calendar event
                var testNotification = new WebhookNotification
                {
                    CalendarId = "test-calendar-id",
                    EventType = "updated",
                    ResourceId = $"test-resource-{Guid.NewGuid()}",
                    ResourceUri = "https://www.googleapis.com/calendar/v3/calendars/test-calendar/events",
                    ChannelId = $"test-channel-{Guid.NewGuid()}",
                    ChannelToken = $"test-token-{Guid.NewGuid()}",
                    ReceivedAt = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(testNotification);
                var body = Encoding.UTF8.GetBytes(json);

                _channel.BasicPublish(
                    exchange: _settings.ExchangeName,
                    routingKey: _settings.RoutingKey,
                    basicProperties: null,
                    body: body);

                _logger.LogInformation("Published test webhook notification to RabbitMQ: CalendarId={CalendarId}, EventType={EventType}", 
                    testNotification.CalendarId, testNotification.EventType);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish test webhook notification to RabbitMQ");
                return false;
            }
        }

        public async Task<(bool IsHealthy, string Status, DateTime LastCheck)> GetHealthAsync()
        {
            _lastHealthCheck = DateTime.UtcNow;

            try
            {
                if (!IsConnected)
                {
                    _lastHealthStatus = false;
                    _lastHealthMessage = "Not connected to RabbitMQ";
                }
                else
                {
                    // Test connection by checking if we can declare a temporary queue
                    var testQueueName = $"health_check_{Guid.NewGuid():N}";
                    _channel.QueueDeclare(testQueueName, false, true, true);
                    _channel.QueueDelete(testQueueName);
                    
                    _lastHealthStatus = true;
                    _lastHealthMessage = "Connected and operational";
                }
            }
            catch (Exception ex)
            {
                _lastHealthStatus = false;
                _lastHealthMessage = $"Health check failed: {ex.Message}";
                _logger.LogWarning(ex, "RabbitMQ health check failed");
            }

            return (_lastHealthStatus, _lastHealthMessage, _lastHealthCheck);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
                _disposed = true;
            }
        }
    }
}