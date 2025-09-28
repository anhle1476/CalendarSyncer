using CalendarSync.Models;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    public class CalendarWrapper : ICalendarWrapper
    {
        private readonly CalendarService _calendarService;
        private readonly GoogleSettings _googleSettings;
        private readonly ILogger<CalendarWrapper> logger;

        public CalendarWrapper(IOptions<GoogleSettings> googleSettings, CalendarService calendarService, ILogger<CalendarWrapper> logger)
        {
            _googleSettings = googleSettings.Value;
            _calendarService = calendarService;
            this.logger = logger;
        }

        public async Task<Events> ListEventsAsync(CancellationToken cancellationToken)
        {
            var request = _calendarService.Events.List(_googleSettings.CalendarId);
            return await request.ExecuteAsync(cancellationToken);
        }

        public async Task EnsureCalendarExistsAsync(CancellationToken cancellationToken)
        {
            try
            {
                // First, try to get the calendar to see if we have access
                await _calendarService.CalendarList.Get(_googleSettings.CalendarId).ExecuteAsync(cancellationToken);
            }
            catch (Google.GoogleApiException e) when (e.Error.Code == 404)
            {
                // If the calendar is not found, it means the service account has not accepted the share yet.
                // We need to add it to the service account's calendar list.
                var calendarListEntry = new CalendarListEntry
                {
                    Id = _googleSettings.CalendarId
                };

                await _calendarService.CalendarList.Insert(calendarListEntry).ExecuteAsync(cancellationToken);
                logger.LogInformation("Successfully added calendar '{CalendarId}' to service account's calendar list.", _googleSettings.CalendarId);
            }
        }
    }
}