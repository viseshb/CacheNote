using System.Globalization;
using Dapper;
using StickyDesk.Core.Models;

namespace StickyDesk.Core.Data;

public interface INoteRepository
{
    Note? GetById(long id);
    long Insert(Note note);
    void UpdateContent(long id, string title, byte[]? contentRtf, string contentPlain);

    /// <summary>Active (not deleted, not archived) notes, pinned first then most-recently updated.</summary>
    IReadOnlyList<Note> GetAllActive();

    /// <summary>Active notes that are favorited or pinned (the Favorites section).</summary>
    IReadOnlyList<Note> GetFavoritesAndPinned();

    void SetPinned(long id, bool pinned);
    void SetFavorite(long id, bool favorite);
    void SetArchived(long id, bool archived);
    void SoftDelete(long id);

    /// <summary>M1a prototype helper: the most-recent live note, creating a blank one if none exist.</summary>
    Note GetOrCreateScratch();
}

public sealed class NoteRepository : INoteRepository
{
    private readonly IDbConnectionFactory _factory;

    public NoteRepository(IDbConnectionFactory factory) => _factory = factory;

    public Note? GetById(long id)
    {
        using var conn = _factory.Create();
        return conn.QuerySingleOrDefault<Note>("SELECT * FROM notes WHERE id = @id;", new { id });
    }

    public long Insert(Note note)
    {
        using var conn = _factory.Create();
        var now = Iso(DateTime.UtcNow);
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO notes(title, content_rtf, content_plain, pinned, favorite, is_archived, is_deleted, created_utc, updated_utc, sync_status)
            VALUES (@Title, @ContentRtf, @ContentPlain, @Pinned, @Favorite, @IsArchived, @IsDeleted, @now, @now, 'local');
            SELECT last_insert_rowid();
            """,
            new
            {
                note.Title,
                note.ContentRtf,
                note.ContentPlain,
                note.Pinned,
                note.Favorite,
                note.IsArchived,
                note.IsDeleted,
                now,
            });
    }

    public void UpdateContent(long id, string title, byte[]? contentRtf, string contentPlain)
    {
        using var conn = _factory.Create();
        conn.Execute(
            """
            UPDATE notes
            SET title = @title, content_rtf = @contentRtf, content_plain = @contentPlain, updated_utc = @now
            WHERE id = @id;
            """,
            new { id, title, contentRtf, contentPlain, now = Iso(DateTime.UtcNow) });
    }

    public IReadOnlyList<Note> GetAllActive()
    {
        using var conn = _factory.Create();
        return conn.Query<Note>(
            """
            SELECT * FROM notes
            WHERE is_deleted = 0 AND is_archived = 0
            ORDER BY pinned DESC, updated_utc DESC;
            """).AsList();
    }

    public IReadOnlyList<Note> GetFavoritesAndPinned()
    {
        using var conn = _factory.Create();
        return conn.Query<Note>(
            """
            SELECT * FROM notes
            WHERE is_deleted = 0 AND is_archived = 0 AND (favorite = 1 OR pinned = 1)
            ORDER BY pinned DESC, favorite DESC, updated_utc DESC;
            """).AsList();
    }

    public void SetPinned(long id, bool pinned) => SetFlag(id, "pinned", pinned);
    public void SetFavorite(long id, bool favorite) => SetFlag(id, "favorite", favorite);
    public void SetArchived(long id, bool archived) => SetFlag(id, "is_archived", archived);
    public void SoftDelete(long id) => SetFlag(id, "is_deleted", true);

    private void SetFlag(long id, string column, bool value)
    {
        using var conn = _factory.Create();
        conn.Execute(
            $"UPDATE notes SET {column} = @value, updated_utc = @now WHERE id = @id;",
            new { id, value, now = Iso(DateTime.UtcNow) });
    }

    public Note GetOrCreateScratch()
    {
        using var conn = _factory.Create();
        var existing = conn.QuerySingleOrDefault<Note>(
            "SELECT * FROM notes WHERE is_deleted = 0 ORDER BY updated_utc DESC LIMIT 1;");
        if (existing is not null)
            return existing;

        var note = new Note { Title = "" };
        note.Id = Insert(note);
        return GetById(note.Id)!;
    }

    private static string Iso(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);
}
