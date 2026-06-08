namespace StickyDesk.Core.Models;

/// <summary>A Markdown block appended to a note (raw Markdown; rendered in the editor).</summary>
public sealed class MdBlock
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public string Content { get; set; } = "";
    public int SortOrder { get; set; }
}
