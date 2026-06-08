using StickyDesk.Core.Data;
using StickyDesk.Core.Data.Migrations;
using StickyDesk.Core.DependencyInjection;
using StickyDesk.Core.Infrastructure;
using StickyDesk.Core.Models;
using StickyDesk.Core.Services;

namespace StickyDesk.Tests;

/// <summary>Round-trips for the M4/M7 content services (tasks, tags, FTS search, attachments).</summary>
public sealed class ContentServicesTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly SqliteConnectionFactory _factory;
    private readonly NoteRepository _notes;

    public ContentServicesTests()
    {
        CoreServiceCollectionExtensions.ConfigureDapper();
        _root = Path.Combine(Path.GetTempPath(), "stickydesk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _factory = new SqliteConnectionFactory(_paths);
        new MigrationRunner(_factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance).Run();
        _notes = new NoteRepository(_factory);
    }

    [Fact]
    public void Tasks_Create_Complete_Delete()
    {
        var svc = new TaskService(new TaskRepository(_factory));
        var id = svc.Create(noteId: null, "Buy milk", null, DateTime.UtcNow.AddHours(2), "high");

        Assert.Single(svc.GetAll());
        svc.SetCompleted(id, true);
        Assert.True(svc.GetAll().Single().IsCompleted);

        svc.Delete(id);
        Assert.Empty(svc.GetAll());
    }

    [Fact]
    public void Tags_Assign_Query_And_GetOrCreate_IsIdempotent()
    {
        var tags = new TagService(new TagRepository(_factory));
        var noteId = _notes.Insert(new Note { Title = "Note" });

        var tid = tags.GetOrCreate("Work");
        tags.AddToNote(noteId, tid);

        Assert.Contains(tags.GetForNote(noteId), t => t.Name == "Work");
        Assert.Single(tags.GetNotesForTag(tid));
        Assert.Equal(tid, tags.GetOrCreate("work")); // case-insensitive, no duplicate
    }

    [Fact]
    public void Search_FindsNote_ByContent_And_Prefix()
    {
        var id = _notes.Insert(new Note { Title = "Groceries", ContentPlain = "milk eggs bread" });
        var search = new SearchService(_factory);

        Assert.Contains(search.SearchNotes("eggs"), n => n.Id == id);
        Assert.Contains(search.SearchNotes("grocer"), n => n.Id == id); // prefix match
        Assert.Empty(search.SearchNotes("zzzznomatch"));
    }

    [Fact]
    public void Attachments_Save_Get_Remove_TouchesDiskAndDb()
    {
        var svc = new AttachmentService(new AttachmentRepository(_factory), _paths);
        var noteId = _notes.Insert(new Note { Title = "Note" });

        var a = svc.SaveImage(noteId, [1, 2, 3, 4], ".png");
        Assert.True(File.Exists(svc.AbsolutePath(a)));
        Assert.Single(svc.GetForNote(noteId));

        svc.Remove(a.Id);
        Assert.Empty(svc.GetForNote(noteId));
        Assert.False(File.Exists(svc.AbsolutePath(a)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
