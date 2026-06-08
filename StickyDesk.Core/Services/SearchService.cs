using System.Linq;
using Dapper;
using StickyDesk.Core.Data;
using StickyDesk.Core.Models;

namespace StickyDesk.Core.Services;

public interface ISearchService
{
    /// <summary>Instant full-text search over note titles + plaintext (FTS5, prefix-matched).</summary>
    IReadOnlyList<Note> SearchNotes(string query);

    /// <summary>Task title/description matches (LIKE) for the global search.</summary>
    IReadOnlyList<TaskItem> SearchTasks(string query);
}

public sealed class SearchService : ISearchService
{
    private readonly IDbConnectionFactory _factory;

    public SearchService(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<Note> SearchNotes(string query)
    {
        var match = BuildFtsMatch(query);
        if (match is null)
            return [];

        using var conn = _factory.Create();
        return conn.Query<Note>(
            """
            SELECT n.* FROM notes_fts f
            JOIN notes n ON n.id = f.rowid
            WHERE notes_fts MATCH @match AND n.is_deleted = 0 AND n.is_archived = 0
            ORDER BY rank;
            """,
            new { match }).AsList();
    }

    public IReadOnlyList<TaskItem> SearchTasks(string query)
    {
        var like = "%" + query.Trim() + "%";
        using var conn = _factory.Create();
        return conn.Query<TaskItem>(
            """
            SELECT * FROM tasks
            WHERE title LIKE @like OR description LIKE @like
            ORDER BY is_completed, created_utc DESC;
            """,
            new { like }).AsList();
    }

    /// <summary>
    /// Turn free text into a safe FTS5 MATCH expression: each word becomes a prefix term
    /// (word*), stripped of punctuation so special characters can't break the query syntax.
    /// </summary>
    private static string? BuildFtsMatch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var terms = query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
            .Where(t => t.Length > 0)
            .Select(t => t + "*");

        var match = string.Join(" ", terms);
        return string.IsNullOrEmpty(match) ? null : match;
    }
}
