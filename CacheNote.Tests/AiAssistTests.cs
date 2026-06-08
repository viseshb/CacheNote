using CacheNote.Core.Ai;
using CacheNote.Core.Cloud;
using CacheNote.Core.Data;
using CacheNote.Core.Data.Migrations;
using CacheNote.Core.DependencyInjection;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Services;

namespace CacheNote.Tests;

/// <summary>Agentic AI chain with the fake client: plan → parse → apply through real repos.</summary>
public sealed class AiAssistTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly SqliteConnectionFactory _factory;

    public AiAssistTests()
    {
        CoreServiceCollectionExtensions.ConfigureDapper();
        _root = Path.Combine(Path.GetTempPath(), "CacheNote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _factory = new SqliteConnectionFactory(_paths);
        new MigrationRunner(_factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance).Run();
    }

    [Fact]
    public void ParseActions_ToleratesCodeFences()
    {
        var raw = "```json\n[ {\"action\":\"add_tag\",\"name\":\"x\"} ]\n```";
        var actions = AiAssistService.ParseActions(raw);
        Assert.Single(actions);
        Assert.Equal("add_tag", actions[0].Action);
    }

    [Fact]
    public async Task FakePlan_Apply_CreatesNoteTaskTag()
    {
        Environment.SetEnvironmentVariable("AI_PROVIDER", "fake");
        try
        {
            var notes = new NoteRepository(_factory);
            var checklist = new ChecklistRepository(_factory);
            var tasks = new TaskService(new TaskRepository(_factory));
            var tags = new TagService(new TagRepository(_factory));
            var executor = new AiActionExecutor(notes, checklist, tasks, tags);

            var cfg = new CloudConfig(_paths);
            Assert.Equal("fake", cfg.AiProvider);
            var svc = new AiAssistService(new GeminiClientFactory(cfg), executor);

            var actions = await svc.PlanAsync("organize my launch");
            Assert.NotEmpty(actions);

            svc.Apply(actions, currentNoteId: null);

            Assert.Contains(notes.GetAllActive(), n => n.Title.Contains("Plan", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(tasks.GetAll());
            Assert.Contains(tags.GetAll(), t => t.Name == "ai");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_PROVIDER", null);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
