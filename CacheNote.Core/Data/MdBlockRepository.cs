using System.Globalization;
using Dapper;
using CacheNote.Core.Models;

namespace CacheNote.Core.Data;

public interface IMdBlockRepository
{
    IReadOnlyList<MdBlock> GetByNote(long noteId);
    long Add(long noteId, string content, int sortOrder);
    void UpdateContent(long id, string content);
    void Delete(long id);
}

public sealed class MdBlockRepository : IMdBlockRepository
{
    private readonly IDbConnectionFactory _factory;

    public MdBlockRepository(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<MdBlock> GetByNote(long noteId)
    {
        using var conn = _factory.Create();
        return conn.Query<MdBlock>(
            "SELECT * FROM note_md_blocks WHERE note_id = @noteId ORDER BY sort_order, id;",
            new { noteId }).AsList();
    }

    public long Add(long noteId, string content, int sortOrder)
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO note_md_blocks(note_id, content, sort_order, created_utc)
            VALUES (@noteId, @content, @sortOrder, @now);
            SELECT last_insert_rowid();
            """,
            new { noteId, content, sortOrder, now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
    }

    public void UpdateContent(long id, string content)
    {
        using var conn = _factory.Create();
        conn.Execute("UPDATE note_md_blocks SET content = @content WHERE id = @id;", new { id, content });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM note_md_blocks WHERE id = @id;", new { id });
    }
}
