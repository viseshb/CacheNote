using System.Globalization;
using Dapper;
using CacheNote.Core.Models;

namespace CacheNote.Core.Data;

public interface IAttachmentRepository
{
    IReadOnlyList<Attachment> GetByNote(long noteId);
    long Insert(Attachment a);
    Attachment? GetById(long id);
    void Delete(long id);
}

public sealed class AttachmentRepository : IAttachmentRepository
{
    private readonly IDbConnectionFactory _factory;

    public AttachmentRepository(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<Attachment> GetByNote(long noteId)
    {
        using var conn = _factory.Create();
        return conn.Query<Attachment>(
            "SELECT * FROM attachments WHERE note_id = @noteId ORDER BY id;",
            new { noteId }).AsList();
    }

    public long Insert(Attachment a)
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO attachments(note_id, filename, rel_path, mime, size_bytes, created_utc)
            VALUES (@NoteId, @Filename, @RelPath, @Mime, @SizeBytes, @now);
            SELECT last_insert_rowid();
            """,
            new { a.NoteId, a.Filename, a.RelPath, a.Mime, a.SizeBytes, now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
    }

    public Attachment? GetById(long id)
    {
        using var conn = _factory.Create();
        return conn.QuerySingleOrDefault<Attachment>("SELECT * FROM attachments WHERE id = @id;", new { id });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM attachments WHERE id = @id;", new { id });
    }
}
