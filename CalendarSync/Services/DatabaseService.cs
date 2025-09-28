using CalendarSync.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace CalendarSync.Services
{
    /// <summary>
    /// Service for interacting with the database.
    /// </summary>
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(IOptions<DatabaseSettings> databaseSettings)
        {
            _connectionString = databaseSettings.Value.ConnectionString;
        }

        /// <summary>
        /// Gets the last sync token for a specific calendar.
        /// </summary>
        /// <param name="calendarId">The ID of the calendar.</param>
        /// <returns>The last sync token.</returns>
        public async Task<string> GetLastSyncTokenAsync(string calendarId)
        {
            const string sql = "SELECT SyncToken FROM CalendarSyncState WHERE CalendarID = @CalendarID;";
            using (var connection = new SqlConnection(_connectionString))
            {
                return await connection.QuerySingleOrDefaultAsync<string>(sql, new { CalendarID = calendarId });
            }
        }

        /// <summary>
        /// Updates the last sync token for a specific calendar.
        /// </summary>
        /// <param name="calendarId">The ID of the calendar.</param>
        /// <param name="syncToken">The new sync token.</param>
        public async Task UpdateLastSyncTokenAsync(string calendarId, string syncToken)
        {
            const string sql = @"
                MERGE CalendarSyncState AS target
                USING (SELECT @CalendarID AS CalendarID) AS source
                ON (target.CalendarID = source.CalendarID)
                WHEN MATCHED THEN
                    UPDATE SET SyncToken = @SyncToken, LastSyncTime = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (CalendarID, SyncToken, LastSyncTime) VALUES (@CalendarID, @SyncToken, GETUTCDATE());
            ";
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.ExecuteAsync(sql, new { CalendarID = calendarId, SyncToken = syncToken });
            }
        }

        /// <summary>
        /// Upserts a calendar event into the database.
        /// </summary>
        /// <param name="calendarEvent">The calendar event to upsert.</param>
        public async Task UpsertEventAsync(CalendarEvent calendarEvent)
        {
            const string sql = @"
                MERGE CalendarEvents AS target
                USING (SELECT @EventID AS EventID) AS source
                ON (target.EventID = source.EventID)
                WHEN MATCHED THEN
                    UPDATE SET
                        Summary = @Summary,
                        Description = @Description,
                        StartTime = @StartTime,
                        EndTime = @EndTime,
                        UpdatedTime = @UpdatedTime,
                        Location = @Location,
                        Status = @Status,
                        OrganizerEmail = @OrganizerEmail,
                        Attendees = @Attendees,
                        Recurrence = @Recurrence
                WHEN NOT MATCHED THEN
                    INSERT (EventID, CalendarID, Summary, Description, StartTime, EndTime, CreatedTime, UpdatedTime, Location, Status, OrganizerEmail, Attendees, Recurrence)
                    VALUES (@EventID, @CalendarID, @Summary, @Description, @StartTime, @EndTime, @CreatedTime, @UpdatedTime, @Location, @Status, @OrganizerEmail, @Attendees, @Recurrence);
            ";

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.ExecuteAsync(sql, calendarEvent);
            }
        }

        /// <summary>
        /// Deletes a calendar event from the database.
        /// </summary>
        /// <param name="eventId">The ID of the event to delete.</param>
        public async Task DeleteEventAsync(string eventId)
        {
            const string sql = "DELETE FROM CalendarEvents WHERE EventID = @EventID;";
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.ExecuteAsync(sql, new { EventID = eventId });
            }
        }
    }
}