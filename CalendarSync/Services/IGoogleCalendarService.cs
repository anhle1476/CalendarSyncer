using Google.Apis.Calendar.v3.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    public interface IGoogleCalendarService
    {
        Task<Events> GetEventsAsync(string syncToken, CancellationToken cancellationToken);
        Task<IList<Event>> GetAllEventsAsync(CancellationToken cancellationToken);
        Task EnsureCalendarExistsAsync(CancellationToken cancellationToken);
    }
}