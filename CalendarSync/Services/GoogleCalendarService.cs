using CalendarSync.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    public class GoogleCalendarService : IGoogleCalendarService
    {
        private readonly ILogger<GoogleCalendarService> _logger;
        private readonly ICalendarWrapper _calendarWrapper;

        public GoogleCalendarService(ILogger<GoogleCalendarService> logger, ICalendarWrapper calendarWrapper)
        {
            _logger = logger;
            _calendarWrapper = calendarWrapper;
        }

        public async Task<IList<Event>> GetEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var events = await _calendarWrapper.ListEventsAsync(cancellationToken);
                return events.Items;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve events.");
                return null;
            }
        }
    }
}