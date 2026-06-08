using Dapper;
using StickyDesk.Core.Models;

namespace StickyDesk.Core.Data;

public interface IChecklistRepository
{
    IReadOnlyList<ChecklistItem> GetByNote(long noteId);
    long Add(long noteId, string text, int sortOrder);
    void UpdateText(long id, string text);
    void SetDone(long id, bool done);
    void Delete(long id);
}

public sealed class ChecklistRepository : IChecklistRepository
{
    private readonly IDbConnectionFactory _factory;

    public ChecklistRepository(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<ChecklistItem> GetByNote(long noteId)
    {
        using var conn = _factory.Create();
        return conn.Query<ChecklistItem>(
            "SELECT * FROM checklist_items WHERE note_id = @noteId ORDER BY sort_order, id;",
            new { noteId }).AsList();
    }

    public long Add(long noteId, string text, int sortOrder)
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO checklist_items(note_id, text, is_done, sort_order)
            VALUES (@noteId, @text, 0, @sortOrder);
            SELECT last_insert_rowid();
            """,
            new { noteId, text, sortOrder });
    }

    public void UpdateText(long id, string text)
    {
        using var conn = _factory.Create();
        conn.Execute("UPDATE checklist_items SET text = @text WHERE id = @id;", new { id, text });
    }

    public void SetDone(long id, bool done)
    {
        using var conn = _factory.Create();
        conn.Execute("UPDATE checklist_items SET is_done = @done WHERE id = @id;", new { id, done });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM checklist_items WHERE id = @id;", new { id });
    }
}
