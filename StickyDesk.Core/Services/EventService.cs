using StickyDesk.Core.Data;
using StickyDesk.Core.Models;

namespace StickyDesk.Core.Services;

/// <summary>
/// Calendar event CRUD with alert wiring: when an event has an alert, a one-shot reminder is
/// created/updated at (next occurrence start − alert minutes) so the existing tray toast engine
/// fires it. The reminder is linked via <see cref="CalendarEvent.ReminderId"/> and removed with the event.
/// </summary>
public sealed class EventService
{
    private readonly IEventRepository _events;
    private readonly IReminderRepository _reminders;

    public EventService(IEventRepository events, IReminderRepository reminders)
    {
        _events = events;
        _reminders = reminders;
    }

    public IReadOnlyList<CalendarEvent> GetAll() => _events.GetAll();

    public CalendarEvent? GetById(long id) => _events.GetById(id);

    /// <summary>Insert (Id == 0) or update an event, then sync its alert reminder.</summary>
    public long Save(CalendarEvent e)
    {
        e.UpdatedUtc = DateTime.UtcNow;
        if (e.Id == 0)
        {
            e.CreatedUtc = DateTime.UtcNow;
            e.Id = _events.Insert(e);
        }
        else
        {
            _events.Update(e);
        }
        SyncAlert(e);
        return e.Id;
    }

    public void Delete(long id)
    {
        var e = _events.GetById(id);
        if (e?.ReminderId is long rid)
            _reminders.Delete(rid);
        _events.Delete(id);
    }

    private void SyncAlert(CalendarEvent e)
    {
        if (e.AlertMinutes is null)
        {
            if (e.ReminderId is long rid)
            {
                _reminders.Delete(rid);
                _events.SetReminderId(e.Id, null);
            }
            return;
        }

        var nextStart = Recurrence.NextStartUtc(e.StartUtc, e.Recurrence, DateTime.UtcNow.AddMinutes(-1));
        var fire = nextStart.AddMinutes(-e.AlertMinutes.Value);
        var message = $"\U0001F4C5 {(string.IsNullOrWhiteSpace(e.Title) ? "Event" : e.Title)}";

        if (e.ReminderId is long existing && _reminders.GetById(existing) is not null)
        {
            _reminders.UpdateDetails(existing, message, fire, RepeatKinds.Once, fire);
        }
        else
        {
            var id = _reminders.Insert(new Reminder
            {
                RemindUtc = fire,
                NextFireUtc = fire,
                Message = message,
                Repeat = RepeatKinds.Once,
                IsDismissed = false,
            });
            _events.SetReminderId(e.Id, id);
        }
    }
}
