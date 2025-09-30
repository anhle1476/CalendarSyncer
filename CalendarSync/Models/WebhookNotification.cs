using System;
using System.Text.Json.Serialization;

namespace CalendarSync.Models
{
    /// <summary>
    /// Represents a webhook notification from Google Calendar API.
    /// This is a simplified notification that only triggers a sync operation.
    /// The actual event data is retrieved through incremental sync using the stored sync token.
    /// </summary>
    public class WebhookNotification
    {
        /// <summary>
        /// Calendar ID that the notification is for
        /// </summary>
        [JsonPropertyName("calendarId")]
        public string CalendarId { get; set; } = string.Empty;

        /// <summary>
        /// Type of event that occurred (created, updated, deleted)
        /// </summary>
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        /// <summary>
        /// Resource ID from Google Calendar webhook
        /// </summary>
        [JsonPropertyName("resourceId")]
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Resource URI from Google Calendar webhook
        /// </summary>
        [JsonPropertyName("resourceUri")]
        public string ResourceUri { get; set; } = string.Empty;

        /// <summary>
        /// Channel ID for the webhook subscription
        /// </summary>
        [JsonPropertyName("channelId")]
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>
        /// Channel token for webhook verification
        /// </summary>
        [JsonPropertyName("channelToken")]
        public string ChannelToken { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when the webhook was received
        /// </summary>
        [JsonPropertyName("receivedAt")]
        public DateTime ReceivedAt { get; set; }
    }
}