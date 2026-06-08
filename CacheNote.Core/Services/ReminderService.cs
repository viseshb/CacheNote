using CacheNote.Core.Data;
using CacheNote.Core.Models;

namespace CacheNote.Core.Services;

/// <summary>Pure next-fire math for repeating reminders (no I/O — unit tested directly).</summary>
public static class ReminderMath
{
    public static DateTime NextOccurrence(DateTime from, string repeat) => repeat switch
    {
        RepeatKinds.Daily => from.AddDays(1),
        RepeatKinds.Weekly => from.AddDays(7),
        RepeatKinds.Monthly => from.AddMonths(1),
        _ => from,
    };

    /// <summary>
    /// Advance a fire time forward by its repeat interval until it is strictly after
    /// <paramref name="now"/>. This collapses a long gap (e.g. the app was closed for a
    /// week) into a single next fire instead of a backlog. 'once' never advances.
    /// </summary>
    public static DateTime AdvancePastNow(DateTime fire, string repeat, DateTime now)
    {
        if (repeat == RepeatKinds.Once)
            return fire;

        var next = fire;
        for (var guard = 0; next <= now && guard < 100_000; guard++)
            next = NextOccurrence(next, repeat);
        return next;
    }
}

public interface IReminderService
{
    long Create(long? noteId, string? message, DateTime remindUtc, string repeat);
    IReadOnlyList<Reminder> GetAll();

    /// <summary>Returns reminders due at <paramref name="nowUtc"/> and advances/dismisses each
    /// (repeating ones roll forward, one-shots are dismissed). The returned list is the set to
    /// notify the user about.</summary>
    IReadOnlyList<Reminder> GetDueAndAdvance(DateTime nowUtc);

    void Snooze(long id, int minutes, DateTime nowUtc);
    void Complete(long id);
    void Delete(long id);
}

public sealed class ReminderService : IReminderService
{
    private readonly IReminderRepository _repo;

    public ReminderService(IReminderRepository repo) => _repo = repo;

    public long Create(long? noteId, string? message, DateTime remindUtc, string repeat)
    {
        var utc = remindUtc.ToUniversalTime();
        return _repo.Insert(new Reminder
        {
            NoteId = noteId,
            Message = message,
            RemindUtc = utc,
            Repeat = RepeatKinds.All.Contains(repeat) ? repeat : RepeatKinds.Once,
            NextFireUtc = utc,
            IsDismissed = false,
        });
    }

    public IReadOnlyList<Reminder> GetAll() => _repo.GetAll();

    public IReadOnlyList<Reminder> GetDueAndAdvance(DateTime nowUtc)
    {
        var due = _repo.GetDue(nowUtc);
        foreach (var r in due)
        {
            if (r.Repeat == RepeatKinds.Once)
            {
                // One-shot fired → done (a Snooze from the toast re-activates it).
                _repo.UpdateSchedule(r.Id, r.NextFireUtc, snoozeUntilUtc: null, dismissed: true);
            }
            else
            {
                var baseTime = r.NextFireUtc ?? r.RemindUtc;
                var next = ReminderMath.AdvancePastNow(baseTime, r.Repeat, nowUtc);
                _repo.UpdateSchedule(r.Id, next, snoozeUntilUtc: null, dismissed: false);
            }
        }
        return due;
    }

    public void Snooze(long id, int minutes, DateTime nowUtc)
    {
        var r = _repo.GetById(id);
        if (r is null)
            return;
        // Re-activate (one-shots were dismissed when they fired) and fire again after the snooze.
        _repo.UpdateSchedule(id, r.NextFireUtc, snoozeUntilUtc: nowUtc.AddMinutes(minutes), dismissed: false);
    }

    public void Complete(long id)
    {
        var r = _repo.GetById(id);
        if (r is null)
            return;
        _repo.UpdateSchedule(id, r.NextFireUtc, snoozeUntilUtc: null, dismissed: true);
    }

    public void Delete(long id) => _repo.Delete(id);
}
