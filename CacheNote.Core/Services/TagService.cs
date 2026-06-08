using CacheNote.Core.Data;
using CacheNote.Core.Models;

namespace CacheNote.Core.Services;

public interface ITagService
{
    IReadOnlyList<Tag> GetAll();
    long GetOrCreate(string name, string? colorHex = null);
    void Rename(long id, string name);
    void SetColor(long id, string colorHex);
    void Delete(long id);

    IReadOnlyList<Tag> GetForNote(long noteId);
    void AddToNote(long noteId, long tagId);
    void RemoveFromNote(long noteId, long tagId);
    IReadOnlyList<Note> GetNotesForTag(long tagId);
}

public sealed class TagService : ITagService
{
    private readonly ITagRepository _repo;

    public TagService(ITagRepository repo) => _repo = repo;

    public IReadOnlyList<Tag> GetAll() => _repo.GetAll();
    public long GetOrCreate(string name, string? colorHex = null) => _repo.GetOrCreate(name, colorHex);
    public void Rename(long id, string name) => _repo.Rename(id, name);
    public void SetColor(long id, string colorHex) => _repo.SetColor(id, colorHex);
    public void Delete(long id) => _repo.Delete(id);

    public IReadOnlyList<Tag> GetForNote(long noteId) => _repo.GetForNote(noteId);
    public void AddToNote(long noteId, long tagId) => _repo.AddToNote(noteId, tagId);
    public void RemoveFromNote(long noteId, long tagId) => _repo.RemoveFromNote(noteId, tagId);
    public IReadOnlyList<Note> GetNotesForTag(long tagId) => _repo.GetNotesForTag(tagId);
}
