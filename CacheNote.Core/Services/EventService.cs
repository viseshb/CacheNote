using CacheNote.Core.Data;
using CacheNote.Core.Models;

namespace CacheNote.Core.Services;

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

    /// <summary>
    /// Re-arm fired alerts of RECURRING events. Alerts are one-shot reminders, so after one
    /// fires (and is dismissed by the engine) nothing would ever alert for the next
    /// occurrence — a weekly meeting's alert used to fire exactly once, ever. Call this from
    /// the reminder poll loop.
    /// </summary>
    public void ResyncRecurringAlerts()
    {
        foreach (var e in _events.GetAll())
        {
            if (e.AlertMinutes is null || e.Recurrence == EventRecurrence.None)
                continue;
            var fired = e.ReminderId is not long rid || _reminders.GetById(rid) is not { IsDismissed: false };
            if (fired)
                SyncAlert(e);
        }
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

        var now = DateTime.UtcNow;
        var nextStart = Recurrence.NextStartUtc(e.StartUtc, e.Recurrence, now.AddMinutes(-1));
        var fire = nextStart.AddMinutes(-e.AlertMinutes.Value);

        if (e.Recurrence == EventRecurrence.None)
        {
            // One-off event whose alert moment already passed: don't (re-)arm — saving an old
            // event used to reset is_dismissed=0 with a past fire time → spurious toast in 20s.
            if (fire <= now)
            {
                if (e.ReminderId is long stale)
                {
                    _reminders.Delete(stale);
                    _events.SetReminderId(e.Id, null);
                }
                return;
            }
        }
        else
        {
            // The alert lead time can push the fire before now (it's 8:55, daily 9:00 event,
            // 10-min alert) — target the following occurrence instead of firing instantly.
            for (var guard = 0; fire <= now && guard < 1000; guard++)
            {
                nextStart = Recurrence.NextStartUtc(e.StartUtc, e.Recurrence, nextStart.AddMinutes(1));
                fire = nextStart.AddMinutes(-e.AlertMinutes.Value);
            }
        }

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
