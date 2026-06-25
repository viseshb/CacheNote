using System.Globalization;
using System.Text;
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
        int notes = 0, noteUpdates = 0, checklists = 0, tasks = 0, tags = 0, reminders = 0, events = 0, failed = 0;

        foreach (var a in actions)
        {
            // Guard each action: without this, action 3 of 5 throwing meant actions 1–2 were
            // silently persisted, the user saw only "AI error", and a natural retry duplicated them.
            try
            {
            switch (a.Action)
            {
                case AiActionKinds.CreateNote:
                    var body = a.Body ?? "";
                    lastNoteId = _notes.Insert(new Note
                    {
                        Title = a.Title ?? "Untitled",
                        ContentPlain = body,
                        ContentRtf = PlainTextToRtf(body),
                    });
                    if (a.Favorite == true)
                        _notes.SetFavorite(lastNoteId, true);
                    if (a.Pinned == true)
                        _notes.SetPinned(lastNoteId, true);
                    if (IsHexColor(a.TitleColorHex))
                        _notes.SetTitleColor(lastNoteId, a.TitleColorHex);
                    notes++;
                    break;

                case AiActionKinds.UpdateCurrentNote:
                    if (UpdateCurrentNote(a, currentNoteId))
                        noteUpdates++;
                    break;

                case AiActionKinds.AppendToCurrentNote:
                    if (AppendToCurrentNote(a, currentNoteId))
                        noteUpdates++;
                    break;

                case AiActionKinds.SetCurrentNoteState:
                    if (SetCurrentNoteState(a, currentNoteId))
                        noteUpdates++;
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
            catch
            {
                failed++;
            }
        }

        var summary = $"Applied: {Parts(("note", notes), ("note update", noteUpdates), ("checklist", checklists), ("task", tasks), ("tag", tags), ("reminder", reminders), ("event", events))}.";
        return failed == 0 ? summary : $"{summary} ({failed} action{(failed == 1 ? "" : "s")} failed)";
    }

    private bool UpdateCurrentNote(AiAction a, long? currentNoteId)
    {
        if (currentNoteId is not long id || _notes.GetById(id) is not { } note)
            return false;

        var title = string.IsNullOrWhiteSpace(a.Title) ? note.Title : a.Title!.Trim();
        var body = a.Body ?? note.ContentPlain ?? "";
        _notes.UpdateContent(id, title, PlainTextToRtf(body), body);
        ApplyNoteFlags(id, a);
        return true;
    }

    private bool AppendToCurrentNote(AiAction a, long? currentNoteId)
    {
        if (currentNoteId is not long id || _notes.GetById(id) is not { } note || string.IsNullOrWhiteSpace(a.Body))
            return false;

        var existing = note.ContentPlain ?? "";
        var addition = a.Body!.Trim();
        var body = string.IsNullOrWhiteSpace(existing) ? addition : existing.TrimEnd() + Environment.NewLine + Environment.NewLine + addition;
        _notes.UpdateContent(id, note.Title, PlainTextToRtf(body), body);
        ApplyNoteFlags(id, a);
        return true;
    }

    private bool SetCurrentNoteState(AiAction a, long? currentNoteId)
    {
        if (currentNoteId is not long id || _notes.GetById(id) is null)
            return false;
        ApplyNoteFlags(id, a);
        return a.Favorite is not null || a.Pinned is not null || a.Archived is not null ||
               a.Deleted is not null || IsHexColor(a.TitleColorHex);
    }

    private void ApplyNoteFlags(long id, AiAction a)
    {
        if (a.Favorite is bool favorite)
            _notes.SetFavorite(id, favorite);
        if (a.Pinned is bool pinned)
            _notes.SetPinned(id, pinned);
        if (a.Archived is bool archived)
            _notes.SetArchived(id, archived);
        if (a.Deleted is bool deleted && deleted)
            _notes.SoftDelete(id);
        if (IsHexColor(a.TitleColorHex))
            _notes.SetTitleColor(id, a.TitleColorHex);
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

    private static byte[] PlainTextToRtf(string text)
        => Encoding.UTF8.GetBytes(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs32 " + EscapeRtf(text) + "}");

    private static string EscapeRtf(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\n': sb.Append(@"\par "); break;
                default:
                    if (ch <= 0x7f)
                        sb.Append(ch);
                    else
                        sb.Append(@"\u").Append((short)ch).Append('?');
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool IsHexColor(string? hex)
        => !string.IsNullOrWhiteSpace(hex) &&
           hex.Length == 7 &&
           hex[0] == '#' &&
           hex.Skip(1).All(Uri.IsHexDigit);

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
