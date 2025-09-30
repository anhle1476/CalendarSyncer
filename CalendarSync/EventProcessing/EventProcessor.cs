using CalendarSync.Models;
using CalendarSync.Services;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Options;

namespace CalendarSync.EventProcessing;

/// <summary>
/// Processes calendar events from Google Calendar API
/// </summary>
public class EventProcessor : IEventProcessor
{
    private readonly ILogger<EventProcessor> _logger;
    private readonly IDatabaseService _databaseService;
    private readonly INotificationService _notificationService;
    private readonly GoogleSettings _googleSettings;

    public EventProcessor(
        ILogger<EventProcessor> logger,
        IDatabaseService databaseService,
        INotificationService notificationService,
        IOptions<GoogleSettings> googleSettings)
    {
        _logger = logger;
        _databaseService = databaseService;
        _notificationService = notificationService;
        _googleSettings = googleSettings.Value;
    }

    public async Task ProcessEventsAsync(IList<Event> events, CancellationToken cancellationToken = default)
    {
        if (events == null || events.Count == 0)
        {
            _logger.LogDebug("No events to process");
            return;
        }

        _logger.LogInformation("Processing {Count} calendar events", events.Count);

        foreach (var googleEvent in events)
        {
            await ProcessEventAsync(googleEvent, cancellationToken);
        }

        _logger.LogInformation("Completed processing {Count} calendar events", events.Count);
    }

    public async Task ProcessEventAsync(Event calendarEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Processing event {EventId} with status {Status}", calendarEvent.Id, calendarEvent.Status);
            
            if (calendarEvent.Status == "cancelled")
            {
                var wasDeleted = await _databaseService.DeleteEventAsync(calendarEvent.Id);
                if (wasDeleted)
                {
                    _logger.LogInformation("Deleted event {EventId} from local database", calendarEvent.Id);
                    
                    // Send UDP notification for deleted event
                    await _notificationService.SendEventChangeNotificationAsync(calendarEvent.Id, "deleted");
                }
                else
                {
                    _logger.LogDebug("Event {EventId} was not found in local database (already deleted or never existed)", calendarEvent.Id);
                }
            }
            else
            {
                // Convert Google Calendar event to local CalendarEvent model
                var localEvent = ConvertToCalendarEvent(calendarEvent);
                
                // Upsert the event (insert or update)
                var wasUpdated = await _databaseService.UpsertEventAsync(localEvent);
                
                var action = wasUpdated == "updated" ? "updated" : "created";
                _logger.LogInformation("{Action} event {EventId}: {Summary}", 
                    action.Substring(0, 1).ToUpper() + action.Substring(1), 
                    localEvent.EventID, 
                    localEvent.Summary);
                
                // Send UDP notification for created/updated event
                await _notificationService.SendEventChangeNotificationAsync(localEvent.EventID, action);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventId}", calendarEvent.Id);
        }
    }

    public async Task<string?> GetLastSyncTokenAsync(string calendarId)
    {
        return await _databaseService.GetLastSyncTokenAsync(calendarId);
    }

    public async Task UpdateSyncTokenAsync(string calendarId, string syncToken)
    {
        await _databaseService.UpdateLastSyncTokenAsync(calendarId, syncToken);
    }

    /// <summary>
    /// Converts a Google Calendar Event to a local CalendarEvent model
    /// </summary>
    /// <param name="googleEvent">The Google Calendar event</param>
    /// <returns>Local CalendarEvent model</returns>
    private CalendarEvent ConvertToCalendarEvent(Event googleEvent)
    {
        return new CalendarEvent
        {
            EventID = googleEvent.Id ?? string.Empty,
            Summary = googleEvent.Summary ?? string.Empty,
            Description = googleEvent.Description ?? string.Empty,
            Location = googleEvent.Location,
            StartTime = ParseDateTime(googleEvent.Start),
            EndTime = ParseDateTime(googleEvent.End),
            Status = googleEvent.Status ?? "confirmed",
            CreatedTime = googleEvent.Created ?? DateTime.UtcNow,
            UpdatedTime = googleEvent.Updated ?? DateTime.UtcNow,
            CalendarID = _googleSettings.CalendarId,
            OrganizerEmail = googleEvent.Organizer?.Email,
            Attendees = googleEvent.Attendees != null ? string.Join(",", googleEvent.Attendees.Select(a => a.Email ?? string.Empty)) : null,
            Recurrence = googleEvent.Recurrence != null ? string.Join(";", googleEvent.Recurrence) : null
        };
    }

    /// <summary>
    /// Parses Google Calendar EventDateTime to local DateTime
    /// </summary>
    /// <param name="eventDateTime">Google Calendar EventDateTime</param>
    /// <returns>Parsed DateTime or current UTC time if parsing fails</returns>
    private DateTime ParseDateTime(EventDateTime? eventDateTime)
    {
        if (eventDateTime == null)
        {
            return DateTime.UtcNow;
        }

        // Handle all-day events (date only)
        if (!string.IsNullOrEmpty(eventDateTime.Date))
        {
            if (DateTime.TryParse(eventDateTime.Date, out var dateOnly))
            {
                return dateOnly.Date;
            }
        }

        // Handle date-time events
        if (eventDateTime.DateTime.HasValue)
        {
            return eventDateTime.DateTime.Value.ToUniversalTime();
        }

        // Fallback to current time
        _logger.LogWarning("Could not parse EventDateTime, using current UTC time as fallback");
        return DateTime.UtcNow;
    }
}