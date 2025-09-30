namespace CalendarSync.Models
{
    /// <summary>
    /// Configuration model for Google Calendar API settings
    /// </summary>
    public class GoogleSettings
    {
        public string ServiceAccountKeyPath { get; set; } = string.Empty;
        public string CalendarId { get; set; } = "primary";
    }

    /// <summary>
    /// Configuration model for database connection settings
    /// </summary>
    public class DatabaseSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
    }

    /// <summary>
    /// Configuration model for synchronization settings
    /// </summary>
    public class SyncSettings
    {
        /// <summary>
        /// Normal polling interval in minutes when webhook is healthy
        /// </summary>
        public int NormalPollingIntervalMinutes { get; set; } = 30;
        
        /// <summary>
        /// Fallback polling interval in minutes when webhook is unhealthy
        /// </summary>
        public int FallbackPollingIntervalMinutes { get; set; } = 5;
        
        /// <summary>
        /// Legacy interval property for backward compatibility (deprecated)
        /// </summary>
        [Obsolete("Use NormalPollingIntervalMinutes instead")]
        public int IntervalMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Configuration model for UDP notification settings
    /// </summary>
    public class NotificationSettings
    {
        public string UdpHost { get; set; } = "127.0.0.1";
        public int UdpPort { get; set; } = 11004;
    }

    /// <summary>
    /// Configuration model for RabbitMQ message queue settings
    /// </summary>
    public class RabbitMQSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
        public string ExchangeName { get; set; } = "calendar_exchange";
        public string QueueName { get; set; } = "calendar_events";
        public string RoutingKey { get; set; } = "calendar.event";
        public bool Durable { get; set; } = true;
        public int ConnectionTimeoutSeconds { get; set; } = 30;
        public int HeartbeatSeconds { get; set; } = 60;
    }

    /// <summary>
    /// Configuration model for webhook integration settings
    /// </summary>
    public class WebhookSettings
    {
        public bool Enabled { get; set; } = false;
        public string ServiceUrl { get; set; } = "http://localhost:3000";
        public string HealthCheckEndpoint { get; set; } = "/health";
        public int HealthCheckIntervalSeconds { get; set; } = 30;
        public int TimeoutSeconds { get; set; } = 10;
        public bool FallbackToPolling { get; set; } = true;
        /// <summary>
        /// Debounce delay in milliseconds to prevent redundant sync API calls when multiple events are received rapidly
        /// </summary>
        public int DebounceDelayMs { get; set; } = 2000;
    }

    /// <summary>
    /// Root configuration model containing all application settings
    /// </summary>
    public class AppSettings
    {
        public GoogleSettings Google { get; set; } = new();
        public DatabaseSettings Database { get; set; } = new();
        public SyncSettings Sync { get; set; } = new();
        public NotificationSettings Notification { get; set; } = new();
        public RabbitMQSettings RabbitMQ { get; set; } = new();
        public WebhookSettings Webhook { get; set; } = new();
    }
}