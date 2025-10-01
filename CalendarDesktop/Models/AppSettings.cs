namespace CalendarDesktop.Models
{
    /// <summary>
    /// Configuration model for database connection settings
    /// </summary>
    public class DatabaseSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
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
    /// Configuration model for debounce settings
    /// </summary>
    public class DebounceSettings
    {
        public int EventChangeDelayMs { get; set; } = 1500;
    }

    /// <summary>
    /// Root configuration model containing minimal application settings for desktop app
    /// </summary>
    public class AppSettings
    {
        public DatabaseSettings Database { get; set; } = new();
        public NotificationSettings Notification { get; set; } = new();
        public DebounceSettings Debounce { get; set; } = new();
    }
}