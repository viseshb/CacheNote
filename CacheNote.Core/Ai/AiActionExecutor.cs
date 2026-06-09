using System.Globalization;
using CacheNote.Core.Data;
using CacheNote.Core.Models;
using CacheNote.Core.Services;

namespace CacheNote.Core.Ai;

/// <summary>
/// Applies an approved AI action list through the SAME repositories/services the UI uses — the AI
/// never writes to the DB directly. Checklist/tag actions attach to the just-created note (or the
/// current note if none was created). Reminders also land on the calendar, and calendar events with
/// an alert fire through the reminder engine, so "remind me…" shows up in BOTH places.
/// </summary>
public sealed class AiActionExecutor
{
    private readonly INoteRepository _notes;
    private readonly IChecklistRepository _checklist;
    private readonly ITaskService _tasks;
    private readonly ITagService _tags;
    private readonly EventService _events;
    private readonly IReminderService _reminders;

    public AiActionExecutor(
        INoteRepository notes,
        IChecklistRepository checklist,
        ITaskService tasks,
        ITagService tags,
        EventService events,
        IReminderService reminders)
    {
        _notes = notes;
        _checklist = checklist;
        _tasks = tasks;
        _tags = tags;
        _events = events;
        _reminders = reminders;
    }

    /// <summary>Returns a short summary of what was applied (starts with "Applied").</summary>
    public string Apply(IReadOnlyList<AiAction> actions, long? currentNoteId)
    {
        long lastNoteId = currentNoteId ?? 0;
        int notes = 0, checklists = 0, tasks = 0, tags = 0, reminders = 0, events = 0;

        foreach (var a in actions)
        {
            switch (a.Action)
            {
                case AiActionKinds.CreateNote:
                    lastNoteId = _notes.Insert(new Note
                    {
                        Title = a.Title ?? "Untitled",
                        ContentPlain = a.Body ?? "",
                    });
                    if (a.Favorite == true)
                        _notes.SetFavorite(lastNoteId, true);
                    notes++;
                    break;

                case AiActionKinds.AddChecklist:
                    var target = lastNoteId != 0 ? lastNoteId : currentNoteId ?? 0;
                    if (target != 0 && a.Items is { Count: > 0 })
                    {
                        var order = _checklist.GetByNote(target).Count;
                        foreach (var item in a.Items)
                            _checklist.Add(target, item, order++);
                        checklists++;
                    }
                    break;

                case AiActionKinds.CreateTask:
                    _tasks.Create(noteId: null, a.Title ?? "Task", a.Body, ParseDue(a.Due),
                        TaskPriorities.All.Contains(a.Priority) ? a.Priority! : TaskPriorities.Medium);
                    tasks++;
                    break;

                case AiActionKinds.AddTag:
                    if (!string.IsNullOrWhiteSpace(a.Name))
                    {
                        var tid = _tags.GetOrCreate(a.Name);
                        var noteForTag = lastNoteId != 0 ? lastNoteId : currentNoteId ?? 0;
                        if (noteForTag != 0)
                            _tags.AddToNote(noteForTag, tid);
                        tags++;
                    }
                    break;

                case AiActionKinds.CreateReminder:
                    CreateReminder(a);
                    reminders++;
                    break;

                case AiActionKinds.CreateEvent:
                    CreateEvent(a);
                    events++;
                    break;
            }
        }

        return $"Applied: {Parts(("note", notes), ("checklist", checklists), ("task", tasks), ("tag", tags), ("reminder", reminders), ("event", events))}.";
    }

    // A reminder: schedule the nudge AND drop it on the calendar (no extra alert there) so it shows in both.
    private void CreateReminder(AiAction a)
    {
        var whenUtc = ParseWhen(a.Date, a.Time) ?? DateTime.UtcNow.AddHours(1);
        var repeat = RepeatKinds.All.Contains(a.Repeat) ? a.Repeat! : RepeatKinds.Once;
        var title = string.IsNullOrWhiteSpace(a.Title) ? "Reminder" : a.Title!.Trim();

        _reminders.Create(noteId: null, title, whenUtc, repeat);
        _events.Save(new CalendarEvent
        {
            Title = title,
            StartUtc = whenUtc,
            AllDay = string.IsNullOrWhiteSpace(a.Time),
            Kind = EventKinds.Appointment,
            Recurrence = RepeatToRecurrence(repeat),
        });
    }

    // A calendar event; if it has an alert, EventService also creates a linked reminder (both surfaces).
    private void CreateEvent(AiAction a)
    {
        var kind = EventKinds.All.Contains(a.Kind) ? a.Kind! : EventKinds.Event;
        var startUtc = ParseWhen(a.Date, a.Time) ?? DateTime.UtcNow.Date.AddHours(9);
        var allDay = string.IsNullOrWhiteSpace(a.Time) || kind == EventKinds.Birthday;
        var recurrence = EventRecurrence.All.Contains(a.Recurrence) ? a.Recurrence!
            : kind == EventKinds.Birthday ? EventRecurrence.Yearly : EventRecurrence.None;

        // Birthdays / appointments / meetings should nudge: if the model didn't set an alert, default
        // to one so the event also fires a reminder (the "reminder = calendar + reminder" intent).
        var alert = a.AlertMinutes;
        if (alert is null && kind is EventKinds.Birthday or EventKinds.Appointment or EventKinds.Meeting)
            alert = 0;

        _events.Save(new CalendarEvent
        {
            Title = string.IsNullOrWhiteSpace(a.Title) ? "Event" : a.Title!.Trim(),
            StartUtc = startUtc,
            EndUtc = allDay ? null : startUtc.AddHours(1),
            AllDay = allDay,
            Kind = kind,
            Recurrence = recurrence,
            Location = string.IsNullOrWhiteSpace(a.Location) ? null : a.Location!.Trim(),
            MeetingUrl = string.IsNullOrWhiteSpace(a.MeetingUrl) ? null : a.MeetingUrl!.Trim(),
            AlertMinutes = alert,   // set => EventService creates a linked reminder too
        });
    }

    private static string RepeatToRecurrence(string repeat) => repeat switch
    {
        RepeatKinds.Daily => EventRecurrence.Daily,
        RepeatKinds.Weekly => EventRecurrence.Weekly,
        RepeatKinds.Monthly => EventRecurrence.Monthly,
        _ => EventRecurrence.None,
    };

    private static string Parts(params (string Label, int Count)[] items)
    {
        var present = items.Where(i => i.Count > 0).Select(i => $"{i.Count} {i.Label}{(i.Count == 1 ? "" : "s")}").ToList();
        return present.Count == 0 ? "nothing" : string.Join(", ", present);
    }

    private static DateTime? ParseDue(string? due)
        => DateTime.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt.ToUniversalTime()
            : null;

    /// <summary>Combine a YYYY-MM-DD date and optional HH:MM time (local) into a UTC instant.</summary>
    private static DateTime? ParseWhen(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date) ||
            !DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            return null;

        var when = d.Date;
        if (!string.IsNullOrWhiteSpace(time) && TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var t))
            when = when.Add(t);

        return DateTime.SpecifyKind(when, DateTimeKind.Local).ToUniversalTime();
    }
}
