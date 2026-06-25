namespace CacheNote.Core.Ai;

/// <summary>
/// Lightweight, best-effort intent classification for the in-app assistant. Pure string logic so it
/// can be unit-tested and reused away from the WinUI code-behind. These are heuristics, not a parser:
/// they route a request toward the chat-only path, a clarifying question, or the agentic planner.
/// </summary>
public static class AiIntent
{
    public static bool IsSummaryRequest(string text)
        => ContainsAny(text, "summarize", "summary", "overview", "recap");

    public static bool IsRephraseRequest(string text)
        => ContainsAny(text, "rephrase", "rewrite", "word better", "make clearer", "improve wording");

    /// <summary>A question/lookup with no action verb — answer in chat, change nothing.</summary>
    public static bool IsReadOnlyRequest(string text)
    {
        if (ContainsAny(text, "create", "add", "make", "set", "schedule", "remind", "delete", "complete",
                "archive", "pin", "favorite", "unfavorite", "update", "edit", "change", "move", "snooze"))
            return false;
        return ContainsAny(text, "what", "which", "who", "when", "where", "why", "how many", "show", "list",
            "tell me", "summarize", "summary", "overview", "recap", "status", "review", "explain", "do i have");
    }

    /// <summary>A bare "create a note/task/…" with no specifics — needs one clarifying detail before acting.</summary>
    public static bool IsBareCreateRequest(string text, params string[] nouns)
    {
        var lower = NormalizeForIntent(text);
        if (!ContainsAny(lower, "create", "make", "add", "new", "start"))
            return false;
        if (!nouns.Any(n => lower.Contains(n)))
            return false;
        if (ContainsAny(lower, "called", "named", "title", "about", "for ", "to ", "tomorrow", "today", "next ", " at ", " on ", ":"))
            return false;
        return lower.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6;
    }

    public static bool ContainsAny(string text, params string[] needles)
    {
        var lower = text.ToLowerInvariant();
        return needles.Any(lower.Contains);
    }

    /// <summary>Lowercase and collapse punctuation (keep ':') to spaces so word checks are stable.</summary>
    public static string NormalizeForIntent(string text)
        => new(text.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ch == ':' ? ch : ' ')
            .ToArray());
}
