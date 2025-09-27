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

        public CalendarWrapper(IOptions<AppSettings> appSettings, CalendarService calendarService)
        {
            _googleSettings = appSettings.Value.Google;
            _calendarService = calendarService;
        }

        public async Task<Events> ListEventsAsync(CancellationToken cancellationToken)
        {
            var request = _calendarService.Events.List(_googleSettings.CalendarId);
            return await request.ExecuteAsync(cancellationToken);
        }
    }
}