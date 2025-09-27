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
    /// Root configuration model containing all application settings
    /// </summary>
    public class AppSettings
    {
        public GoogleSettings Google { get; set; } = new();
        public DatabaseSettings Database { get; set; } = new();
        public SyncSettings Sync { get; set; } = new();
        public NotificationSettings Notification { get; set; } = new();
    }
}