using CalendarSync.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace CalendarSync.Services
{
    /// <summary>
    /// Service for interacting with the database.
    /// </summary>
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(
            IOptions<DatabaseSettings> databaseSettings,
            ILogger<DatabaseService> logger)
        {
            _connectionString = databaseSettings.Value.ConnectionString;
            _logger = logger;
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
        /// <returns>Returns "added" if event was inserted, "updated" if event was modified.</returns>
        public async Task<string> UpsertEventAsync(CalendarEvent calendarEvent)
        {
            const string checkExistsSql = "SELECT COUNT(1) FROM CalendarEvents WHERE EventID = @EventID;";
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
                // Check if event exists to determine if this is an update or insert
                var existsCount = await connection.QuerySingleAsync<int>(checkExistsSql, new { EventID = calendarEvent.EventID });
                bool isUpdate = existsCount > 0;

                // Execute the upsert
                await connection.ExecuteAsync(sql, calendarEvent);

                string changeType = isUpdate ? "updated" : "added";
                _logger.LogDebug("Event {EventId} was {ChangeType}", calendarEvent.EventID, changeType);
                
                return changeType;
            }
        }

        /// <summary>
        /// Upserts multiple calendar events into the database in a batch operation.
        /// </summary>
        /// <param name="calendarEvents">The collection of calendar events to upsert.</param>
        /// <returns>Returns a dictionary with event IDs as keys and operation type ("added" or "updated") as values.</returns>
        public async Task<Dictionary<string, string>> UpsertEventsBatchAsync(IEnumerable<CalendarEvent> calendarEvents)
        {
            var eventsList = calendarEvents.ToList();
            if (!eventsList.Any())
            {
                return new Dictionary<string, string>();
            }

            var results = new Dictionary<string, string>();
            
            // Check which events already exist
            var eventIds = eventsList.Select(e => e.EventID).ToList();
            const string checkExistsSql = "SELECT EventID FROM CalendarEvents WHERE EventID IN @EventIds;";
            
            using (var connection = new SqlConnection(_connectionString))
            {
                var existingEventIds = (await connection.QueryAsync<string>(checkExistsSql, new { EventIds = eventIds })).ToHashSet();
                
                // Prepare batch upsert SQL
                const string batchUpsertSql = @"
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

                // Execute batch upsert
                await connection.ExecuteAsync(batchUpsertSql, eventsList);
                
                // Determine operation type for each event
                foreach (var calendarEvent in eventsList)
                {
                    string changeType = existingEventIds.Contains(calendarEvent.EventID) ? "updated" : "added";
                    results[calendarEvent.EventID] = changeType;
                    _logger.LogDebug("Event {EventId} was {ChangeType}", calendarEvent.EventID, changeType);
                }
            }
            
            _logger.LogInformation("Batch upserted {Count} events", eventsList.Count);
            return results;
        }

        /// <summary>
        /// Deletes a calendar event from the database.
        /// </summary>
        /// <param name="eventId">The ID of the event to delete.</param>
        /// <returns>Returns true if event was deleted, false if event was not found.</returns>
        public async Task<bool> DeleteEventAsync(string eventId)
        {
            const string sql = "DELETE FROM CalendarEvents WHERE EventID = @EventID;";
            using (var connection = new SqlConnection(_connectionString))
            {
                var rowsAffected = await connection.ExecuteAsync(sql, new { EventID = eventId });
                
                // Only log if event was actually deleted
                if (rowsAffected > 0)
                {
                    _logger.LogDebug("Event {EventId} was deleted", eventId);
                    return true;
                }
                
                return false;
            }
        }
    }
}