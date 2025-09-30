using Google.Apis.Calendar.v3.Data;

namespace CalendarSync.EventProcessing;

/// <summary>
/// Interface for processing calendar events from Google Calendar API
/// </summary>
public interface IEventProcessor
{
    /// <summary>
    /// Processes a collection of calendar events
    /// </summary>
    /// <param name="events">Collection of Google Calendar events to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task ProcessEventsAsync(IList<Event> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single calendar event
    /// </summary>
    /// <param name="calendarEvent">Google Calendar event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task ProcessEventAsync(Event calendarEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last sync token for a calendar
    /// </summary>
    /// <param name="calendarId">The calendar ID</param>
    /// <returns>The last sync token or null if none exists</returns>
    Task<string?> GetLastSyncTokenAsync(string calendarId);

    /// <summary>
    /// Updates the sync token for a calendar
    /// </summary>
    /// <param name="calendarId">The calendar ID</param>
    /// <param name="syncToken">The new sync token</param>
    /// <returns>Task representing the async operation</returns>
    Task UpdateSyncTokenAsync(string calendarId, string syncToken);
}