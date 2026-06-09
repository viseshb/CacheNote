using Dapper;
using CacheNote.Core.Models;

namespace CacheNote.Core.Data;

public interface ITagRepository
{
    IReadOnlyList<Tag> GetAll();
    long GetOrCreate(string name, string? colorHex);
    void Rename(long id, string name);
    void SetColor(long id, string colorHex);
    void Delete(long id);

    IReadOnlyList<Tag> GetForNote(long noteId);
    void AddToNote(long noteId, long tagId);
    void RemoveFromNote(long noteId, long tagId);
    IReadOnlyList<Note> GetNotesForTag(long tagId);
}

public sealed class TagRepository : ITagRepository
{
    private readonly IDbConnectionFactory _factory;

    public TagRepository(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<Tag> GetAll()
    {
        using var conn = _factory.Create();
        return conn.Query<Tag>("SELECT * FROM tags ORDER BY name COLLATE NOCASE;").AsList();
    }

    public long GetOrCreate(string name, string? colorHex)
    {
        name = name.Trim();
        using var conn = _factory.Create();
        var existing = conn.ExecuteScalar<long?>("SELECT id FROM tags WHERE name = @name COLLATE NOCASE;", new { name });
        if (existing is long id)
            return id;
        return conn.ExecuteScalar<long>(
            "INSERT INTO tags(name, color_hex) VALUES (@name, @color); SELECT last_insert_rowid();",
            new { name, color = string.IsNullOrWhiteSpace(colorHex) ? "#71717A" : colorHex });
    }

    public void Rename(long id, string name)
    {
        name = name.Trim();
        using var conn = _factory.Create();
        // tags.name is UNIQUE — renaming onto an existing tag would throw an unhandled
        // SqliteException into the UI. Skip the no-op/conflict cases instead.
        var taken = conn.ExecuteScalar<long?>(
            "SELECT id FROM tags WHERE name = @name COLLATE NOCASE AND id <> @id;", new { id, name });
        if (taken is not null)
            return;
        conn.Execute("UPDATE tags SET name = @name WHERE id = @id;", new { id, name });
    }

    public void SetColor(long id, string colorHex)
    {
        using var conn = _factory.Create();
        conn.Execute("UPDATE tags SET color_hex = @colorHex WHERE id = @id;", new { id, colorHex });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM tags WHERE id = @id;", new { id }); // note_tags rows cascade
    }

    public IReadOnlyList<Tag> GetForNote(long noteId)
    {
        using var conn = _factory.Create();
        return conn.Query<Tag>(
            """
            SELECT t.* FROM tags t
            JOIN note_tags nt ON nt.tag_id = t.id
            WHERE nt.note_id = @noteId
            ORDER BY t.name COLLATE NOCASE;
            """,
            new { noteId }).AsList();
    }

    public void AddToNote(long noteId, long tagId)
    {
        using var conn = _factory.Create();
        conn.Execute(
            "INSERT OR IGNORE INTO note_tags(note_id, tag_id) VALUES (@noteId, @tagId);",
            new { noteId, tagId });
    }

    public void RemoveFromNote(long noteId, long tagId)
    {
        using var conn = _factory.Create();
        conn.Execute(
            "DELETE FROM note_tags WHERE note_id = @noteId AND tag_id = @tagId;",
            new { noteId, tagId });
    }

    public IReadOnlyList<Note> GetNotesForTag(long tagId)
    {
        using var conn = _factory.Create();
        return conn.Query<Note>(
            """
            SELECT n.* FROM notes n
            JOIN note_tags nt ON nt.note_id = n.id
            WHERE nt.tag_id = @tagId AND n.is_deleted = 0 AND n.is_archived = 0
            ORDER BY n.pinned DESC, n.updated_utc DESC;
            """,
            new { tagId }).AsList();
    }
}
