using Google.Apis.Calendar.v3.Data;

namespace CalendarSync.Services
{
    public interface IGoogleCalendarService
    {
        Task<IList<Event>> GetEventsAsync(CancellationToken cancellationToken);
    }
}