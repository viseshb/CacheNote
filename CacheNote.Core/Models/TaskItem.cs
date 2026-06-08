namespace CacheNote.Core.Models;

/// <summary>The allowed task priorities (matches the DB CHECK constraint on tasks.priority).</summary>
public static class TaskPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static readonly string[] All = [Low, Medium, High];
}

/// <summary>
/// A to-do item. May stand alone or be derived from a note (note_id set). All times UTC.
/// Named TaskItem (not Task) to avoid clashing with System.Threading.Tasks.Task.
/// </summary>
public sealed class TaskItem
{
    public long Id { get; set; }
    public long? NoteId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? DueUtc { get; set; }
    public string Priority { get; set; } = TaskPriorities.Medium;
    public bool IsCompleted { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
