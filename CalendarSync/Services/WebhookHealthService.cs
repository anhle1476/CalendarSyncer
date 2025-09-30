using CalendarSync.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace CalendarSync.Services
{
    /// <summary>
    /// Service for monitoring webhook service health and availability
    /// </summary>
    public class WebhookHealthService : IWebhookHealthService
    {
        private readonly ILogger<WebhookHealthService> _logger;
        private readonly WebhookSettings _settings;
        private readonly HttpClient _httpClient;
        private Timer? _healthCheckTimer;
        private bool _disposed = false;
        private bool _isRunning = false;

        private bool _isHealthy = false;
        private string _lastStatus = "Not checked";
        private DateTime _lastCheck = DateTime.MinValue;
        private TimeSpan _lastResponseTime = TimeSpan.Zero;

        public WebhookHealthService(ILogger<WebhookHealthService> logger, IOptions<WebhookSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds)
            };
        }

        public bool IsWebhookServiceHealthy => _isHealthy;

        public (bool IsHealthy, string Status, DateTime LastCheck, TimeSpan ResponseTime) LastHealthCheck =>
            (_isHealthy, _lastStatus, _lastCheck, _lastResponseTime);

        public event EventHandler<WebhookHealthChangedEventArgs>? HealthStatusChanged;

        public async Task StartHealthChecksAsync(CancellationToken cancellationToken)
        {
            if (_isRunning)
            {
                _logger.LogWarning("Health checks are already running");
                return;
            }

            if (!_settings.Enabled)
            {
                _logger.LogInformation("Webhook service is disabled, skipping health checks");
                return;
            }

            _logger.LogInformation("Starting webhook health checks every {Interval} seconds", 
                _settings.HealthCheckIntervalSeconds);

            _isRunning = true;

            // Perform initial health check
            await CheckHealthAsync();

            // Start periodic health checks
            _healthCheckTimer = new Timer(
                async _ => await CheckHealthAsync(),
                null,
                TimeSpan.FromSeconds(_settings.HealthCheckIntervalSeconds),
                TimeSpan.FromSeconds(_settings.HealthCheckIntervalSeconds));

            // Monitor cancellation token
            cancellationToken.Register(async () => await StopHealthChecksAsync());
        }

        public async Task StopHealthChecksAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            _logger.LogInformation("Stopping webhook health checks");

            _isRunning = false;
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;
        }

        public async Task<(bool IsHealthy, string Status, DateTime LastCheck, TimeSpan ResponseTime)> CheckHealthAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var checkTime = DateTime.UtcNow;
            bool isHealthy = false;
            string status = "Unknown";

            try
            {
                if (!_settings.Enabled)
                {
                    isHealthy = false;
                    status = "Webhook service is disabled";
                }
                else
                {
                    var healthUrl = $"{_settings.ServiceUrl.TrimEnd('/')}{_settings.HealthCheckEndpoint}";
                    _logger.LogDebug("Checking webhook service health at: {Url}", healthUrl);

                    var response = await _httpClient.GetAsync(healthUrl);
                    stopwatch.Stop();

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        isHealthy = true;
                        status = $"Healthy (HTTP {(int)response.StatusCode})";
                        _logger.LogDebug("Webhook service health check successful in {ResponseTime}ms", 
                            stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        isHealthy = false;
                        status = $"Unhealthy (HTTP {(int)response.StatusCode})";
                        _logger.LogWarning("Webhook service health check failed with status: {StatusCode}", 
                            response.StatusCode);
                    }
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                stopwatch.Stop();
                isHealthy = false;
                status = $"Timeout after {_settings.TimeoutSeconds}s";
                _logger.LogWarning("Webhook service health check timed out");
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                isHealthy = false;
                status = $"Connection failed: {ex.Message}";
                _logger.LogWarning(ex, "Webhook service health check connection failed");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                isHealthy = false;
                status = $"Error: {ex.Message}";
                _logger.LogError(ex, "Webhook service health check failed with unexpected error");
            }

            // Update internal state
            var wasHealthy = _isHealthy;
            _isHealthy = isHealthy;
            _lastStatus = status;
            _lastCheck = checkTime;
            _lastResponseTime = stopwatch.Elapsed;

            // Fire event if health status changed
            if (wasHealthy != isHealthy)
            {
                _logger.LogInformation("Webhook service health status changed: {WasHealthy} -> {IsHealthy} ({Status})",
                    wasHealthy, isHealthy, status);

                HealthStatusChanged?.Invoke(this, new WebhookHealthChangedEventArgs
                {
                    IsHealthy = isHealthy,
                    Status = status,
                    Timestamp = checkTime,
                    ResponseTime = stopwatch.Elapsed,
                    WasHealthy = wasHealthy
                });
            }

            return (isHealthy, status, checkTime, stopwatch.Elapsed);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopHealthChecksAsync().Wait(TimeSpan.FromSeconds(5));
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}