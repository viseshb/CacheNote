namespace CacheNote.Core.Ai;

/// <summary>Small text helpers for building AI prompt context. One canonical truncation so the UI
/// layers don't each carry their own near-identical copy.</summary>
public static class AiText
{
    /// <summary>Trim to <paramref name="max"/> characters with an ellipsis. When
    /// <paramref name="collapseWhitespace"/> is set, newlines/runs of whitespace collapse to single
    /// spaces (for one-line list items); otherwise the text's own line breaks are kept.</summary>
    public static string Truncate(string? text, int max, bool collapseWhitespace = false)
    {
        var s = (text ?? "").Trim();
        if (collapseWhitespace)
            s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= max ? s : s[..max] + "...";
    }
}
