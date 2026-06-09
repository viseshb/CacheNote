namespace CacheNote.Core.Models;

/// <summary>A note: rich text (RTF) plus a derived plaintext copy used for search.</summary>
public sealed class Note
{
    public long Id { get; set; }
    public string Title { get; set; } = "";

    /// <summary>RichEditBox RTF, stored as bytes (UTF-8).</summary>
    public byte[]? ContentRtf { get; set; }

    /// <summary>Plaintext derived from the RTF at save time; indexed by FTS5.</summary>
    public string ContentPlain { get; set; } = "";

    /// <summary>"#RRGGBB" title color picked via the font-color tool; null = theme default.</summary>
    public string? TitleColorHex { get; set; }

    public bool Pinned { get; set; }
    public bool Favorite { get; set; }
    public bool IsArchived { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string SyncStatus { get; set; } = "local";
}
