using System.Globalization;
using Dapper;
using CacheNote.Core.Models;

namespace CacheNote.Core.Data;

public interface ITaskRepository
{
    IReadOnlyList<TaskItem> GetAll();
    TaskItem? GetById(long id);
    long Insert(TaskItem task);
    void Update(long id, string title, string? description, DateTime? dueUtc, string priority);
    void SetCompleted(long id, bool completed);
    void Delete(long id);
    IReadOnlyList<TaskItem> GetByNote(long noteId);
}

public sealed class TaskRepository : ITaskRepository
{
    private readonly IDbConnectionFactory _factory;

    public TaskRepository(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<TaskItem> GetAll()
    {
        using var conn = _factory.Create();
        // Open tasks first (soonest due first, nulls last), then completed.
        return conn.Query<TaskItem>(
            """
            SELECT * FROM tasks
            ORDER BY is_completed,
                     CASE WHEN due_utc IS NULL THEN 1 ELSE 0 END,
                     due_utc,
                     created_utc DESC;
            """).AsList();
    }

    public TaskItem? GetById(long id)
    {
        using var conn = _factory.Create();
        return conn.QuerySingleOrDefault<TaskItem>("SELECT * FROM tasks WHERE id = @id;", new { id });
    }

    public long Insert(TaskItem task)
    {
        using var conn = _factory.Create();
        var now = Iso(DateTime.UtcNow);
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO tasks(note_id, title, description, due_utc, priority, is_completed, created_utc, updated_utc)
            VALUES (@NoteId, @Title, @Description, @DueUtc, @Priority, @IsCompleted, @now, @now);
            SELECT last_insert_rowid();
            """,
            new
            {
                task.NoteId,
                task.Title,
                task.Description,
                DueUtc = task.DueUtc.HasValue ? Iso(task.DueUtc.Value) : null,
                Priority = TaskPriorities.All.Contains(task.Priority) ? task.Priority : TaskPriorities.Medium,
                IsCompleted = task.IsCompleted,
                now,
            });
    }

    public void Update(long id, string title, string? description, DateTime? dueUtc, string priority)
    {
        using var conn = _factory.Create();
        conn.Execute(
            """
            UPDATE tasks
            SET title = @title, description = @description, due_utc = @due, priority = @priority, updated_utc = @now
            WHERE id = @id;
            """,
            new
            {
                id,
                title,
                description,
                due = dueUtc.HasValue ? Iso(dueUtc.Value) : null,
                priority = TaskPriorities.All.Contains(priority) ? priority : TaskPriorities.Medium,
                now = Iso(DateTime.UtcNow),
            });
    }

    public void SetCompleted(long id, bool completed)
    {
        using var conn = _factory.Create();
        conn.Execute(
            "UPDATE tasks SET is_completed = @completed, updated_utc = @now WHERE id = @id;",
            new { id, completed, now = Iso(DateTime.UtcNow) });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM tasks WHERE id = @id;", new { id });
    }

    public IReadOnlyList<TaskItem> GetByNote(long noteId)
    {
        using var conn = _factory.Create();
        return conn.Query<TaskItem>(
            "SELECT * FROM tasks WHERE note_id = @noteId ORDER BY is_completed, created_utc DESC;",
            new { noteId }).AsList();
    }

    private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
