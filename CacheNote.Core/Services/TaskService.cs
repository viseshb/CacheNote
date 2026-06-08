using CacheNote.Core.Data;
using CacheNote.Core.Models;

namespace CacheNote.Core.Services;

public interface ITaskService
{
    IReadOnlyList<TaskItem> GetAll();
    long Create(long? noteId, string title, string? description, DateTime? dueUtc, string priority);
    void Update(long id, string title, string? description, DateTime? dueUtc, string priority);
    void SetCompleted(long id, bool completed);
    void Delete(long id);

    /// <summary>Create a task from a note (M4 "convert note → task"). Returns the new task id.</summary>
    long ConvertNoteToTask(long noteId, string title, string? description);
}

public sealed class TaskService : ITaskService
{
    private readonly ITaskRepository _repo;

    public TaskService(ITaskRepository repo) => _repo = repo;

    public IReadOnlyList<TaskItem> GetAll() => _repo.GetAll();

    public long Create(long? noteId, string title, string? description, DateTime? dueUtc, string priority)
        => _repo.Insert(new TaskItem
        {
            NoteId = noteId,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled task" : title.Trim(),
            Description = description,
            DueUtc = dueUtc,
            Priority = priority,
        });

    public void Update(long id, string title, string? description, DateTime? dueUtc, string priority)
        => _repo.Update(id, title, description, dueUtc, priority);

    public void SetCompleted(long id, bool completed) => _repo.SetCompleted(id, completed);

    public void Delete(long id) => _repo.Delete(id);

    public long ConvertNoteToTask(long noteId, string title, string? description)
        => Create(noteId, title, description, dueUtc: null, TaskPriorities.Medium);
}
