using CalendarSync.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    /// <summary>
    /// Defines the contract for a service that interacts with the database.
    /// </summary>
    public interface IDatabaseService
    {
        /// <summary>
        /// Gets the last sync token for a specific calendar.
        /// </summary>
        /// <param name="calendarId">The ID of the calendar.</param>
        /// <returns>The last sync token.</returns>
        Task<string> GetLastSyncTokenAsync(string calendarId);

        /// <summary>
        /// Updates the last sync token for a specific calendar.
        /// </summary>
        /// <param name="calendarId">The ID of the calendar.</param>
        /// <param name="syncToken">The new sync token.</param>
        Task UpdateLastSyncTokenAsync(string calendarId, string syncToken);

        /// <summary>
        /// Upserts a calendar event into the database.
        /// </summary>
        /// <param name="calendarEvent">The calendar event to upsert.</param>
        /// <returns>Returns "added" if event was inserted, "updated" if event was modified.</returns>
        Task<string> UpsertEventAsync(CalendarEvent calendarEvent);

        /// <summary>
        /// Deletes a calendar event from the database.
        /// </summary>
        /// <param name="eventId">The ID of the event to delete.</param>
        /// <returns>Returns true if event was deleted, false if event was not found.</returns>
        Task<bool> DeleteEventAsync(string eventId);
    }
}