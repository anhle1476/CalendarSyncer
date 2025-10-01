using System;

namespace CalendarDesktop.Models
{
    /// <summary>
    /// Represents a calendar event.
    /// </summary>
    public class CalendarEvent
    {
        public string EventID { get; set; }
        public string CalendarID { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime? CreatedTime { get; set; }
        public DateTime? UpdatedTime { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public string? OrganizerEmail { get; set; }
        public string? Attendees { get; set; }
        public string? Recurrence { get; set; }
    }
}