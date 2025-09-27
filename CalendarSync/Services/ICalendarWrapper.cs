using Google.Apis.Calendar.v3.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CalendarSync.Services
{
    public interface ICalendarWrapper
    {
        Task<Events> ListEventsAsync(CancellationToken cancellationToken);
    }
}