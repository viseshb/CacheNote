namespace StickyDesk.Core.Models;

/// <summary>An image (or file) attached to a note. The file lives under the app's attachments/
/// folder; <see cref="RelPath"/> is relative to that folder so moving the app root preserves it.</summary>
public sealed class Attachment
{
    public long Id { get; set; }
    public long NoteId { get; set; }
    public string Filename { get; set; } = "";
    public string RelPath { get; set; } = "";
    public string? Mime { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedUtc { get; set; }
}
