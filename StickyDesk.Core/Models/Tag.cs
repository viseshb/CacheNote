namespace StickyDesk.Core.Models;

/// <summary>A label that can be applied to notes (many-to-many via note_tags).</summary>
public sealed class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#71717A";
}
