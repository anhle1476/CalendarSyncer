using CalendarSync.Models;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Options;

namespace CalendarSync.Services
{
    public class GoogleCalendarService : IGoogleCalendarService
    {
        private readonly CalendarService _calendarService;
        private readonly GoogleSettings _googleSettings;
        private readonly ILogger<GoogleCalendarService> _logger;

        public GoogleCalendarService(IOptions<GoogleSettings> googleSettings, CalendarService calendarService, ILogger<GoogleCalendarService> logger)
        {
            _googleSettings = googleSettings.Value;
            _calendarService = calendarService;
            _logger = logger;
        }

        public async Task<Events> GetEventsAsync(string syncToken, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Fetching events with sync token: {SyncToken}", syncToken);
            return await ListEventsAsync(syncToken, cancellationToken);
        }

        public async Task<IList<Event>> GetAllEventsAsync(CancellationToken cancellationToken)
        {
            var allEvents = new List<Event>();
            try
            {
                string? pageToken = null;

                do
                {
                    var events = await ListAllEventsAsync(pageToken, cancellationToken);
                    if (events.Items != null)
                    {
                        allEvents.AddRange(events.Items);
                    }
                    pageToken = events.NextPageToken;
                } while (pageToken != null);

                return allEvents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve all events.");
                return allEvents;
            }
        }

        public async Task<Events> ListEventsAsync(string syncToken, CancellationToken cancellationToken)
        {
            var request = _calendarService.Events.List(_googleSettings.CalendarId);
            request.SyncToken = syncToken;
            return await request.ExecuteAsync(cancellationToken);
        }

        public async Task<Events> ListAllEventsAsync(string? pageToken, CancellationToken cancellationToken)
        {
            var request = _calendarService.Events.List(_googleSettings.CalendarId);
            if (!string.IsNullOrEmpty(pageToken)) request.PageToken = pageToken;

            return await request.ExecuteAsync(cancellationToken);
        }

        public async Task EnsureCalendarExistsAsync(CancellationToken cancellationToken)
        {
            try
            {
                // First, try to get the calendar to see if we have access
                await _calendarService.CalendarList.Get(_googleSettings.CalendarId).ExecuteAsync(cancellationToken);
            }
            catch (GoogleApiException e) when (e.Error.Code == 404)
            {
                // If the calendar is not found, it means the service account has not accepted the share yet.
                // We need to add it to the service account's calendar list.
                var calendarListEntry = new CalendarListEntry
                {
                    Id = _googleSettings.CalendarId
                };

                await _calendarService.CalendarList.Insert(calendarListEntry).ExecuteAsync(cancellationToken);
                _logger.LogInformation("Successfully added calendar '{CalendarId}' to service account's calendar list.",
                    _googleSettings.CalendarId);
            }
        }
    }
}