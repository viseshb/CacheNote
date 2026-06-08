using System.Globalization;
using StickyDesk.Core.Data;
using StickyDesk.Core.Models;
using StickyDesk.Core.Services;

namespace StickyDesk.Core.Ai;

/// <summary>
/// Applies an approved AI action list through the SAME repositories/services the UI uses — the AI
/// never writes to the DB directly. Checklist/tag actions attach to the just-created note (or the
/// current note if none was created).
/// </summary>
public sealed class AiActionExecutor
{
    private readonly INoteRepository _notes;
    private readonly IChecklistRepository _checklist;
    private readonly ITaskService _tasks;
    private readonly ITagService _tags;

    public AiActionExecutor(INoteRepository notes, IChecklistRepository checklist, ITaskService tasks, ITagService tags)
    {
        _notes = notes;
        _checklist = checklist;
        _tasks = tasks;
        _tags = tags;
    }

    /// <summary>Returns a short summary of what was applied.</summary>
    public string Apply(IReadOnlyList<AiAction> actions, long? currentNoteId)
    {
        long lastNoteId = currentNoteId ?? 0;
        int notes = 0, checklists = 0, tasks = 0, tags = 0;

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
            }
        }

        return $"Applied: {notes} note(s), {checklists} checklist(s), {tasks} task(s), {tags} tag(s).";
    }

    private static DateTime? ParseDue(string? due)
        => DateTime.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt.ToUniversalTime()
            : null;
}
