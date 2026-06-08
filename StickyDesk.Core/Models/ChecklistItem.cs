namespace StickyDesk.Core.Models;

/// <summary>A single tickable item in a note's inline checklist region.</summary>
public sealed class ChecklistItem
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public string Text { get; set; } = "";
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}
