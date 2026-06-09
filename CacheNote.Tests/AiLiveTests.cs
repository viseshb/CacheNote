using CacheNote.Core.Ai;
using CacheNote.Core.Cloud;
using CacheNote.Core.Data;
using CacheNote.Core.Data.Migrations;
using CacheNote.Core.DependencyInjection;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Services;

namespace CacheNote.Tests;

/// <summary>
/// LIVE Gemini/Vertex checks against the real key in the solution .env. Billed + networked, so it
/// only runs when CACHENOTE_RUN_LIVE_AI=1 (and a key is present); otherwise every assertion no-ops.
/// Verifies summarize / rephrase / plan succeed on normal input and degrade gracefully (no hang,
/// no unexpected crash) on empty + oversized input. Never logs the key.
/// </summary>
public sealed class AiLiveTests : IDisposable
{
    private readonly bool _enabled;
    private readonly string _root;
    private readonly AiAssistService _svc;

    public AiLiveTests()
    {
        CoreServiceCollectionExtensions.ConfigureDapper();

        // CloudConfig reads .env from its AppPaths root → point it at the solution folder so the
        // real keys load (a same-named environment variable still overrides, per CloudConfig).
        var cfg = new CloudConfig(new AppPaths(SolutionDir()));
        var factory = new GeminiClientFactory(cfg);

        // Executor needs repos, but Summarize/Rephrase/Plan never touch them — use a throwaway DB.
        _root = Path.Combine(Path.GetTempPath(), "CacheNote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var dbFactory = new SqliteConnectionFactory(new AppPaths(_root));
        new MigrationRunner(dbFactory, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance).Run();
        var reminderRepo = new ReminderRepository(dbFactory);
        var executor = new AiActionExecutor(
            new NoteRepository(dbFactory),
            new ChecklistRepository(dbFactory),
            new TaskService(new TaskRepository(dbFactory)),
            new TagService(new TagRepository(dbFactory)),
            new EventService(new EventRepository(dbFactory), reminderRepo),
            new ReminderService(reminderRepo));

        _svc = new AiAssistService(factory, executor);
        _enabled = Environment.GetEnvironmentVariable("CACHENOTE_RUN_LIVE_AI") == "1" && _svc.IsConfigured;
    }

    [Fact]
    public async Task Summarize_Rephrase_Plan_Work_On_Normal_Input()
    {
        if (!_enabled) return;

        var summary = await _svc.SummarizeAsync(
            "Our Q3 product launch spans three markets, needs a press kit, a demo video, and a pricing page update.");
        Assert.False(string.IsNullOrWhiteSpace(summary));

        var rephrased = await _svc.RephraseAsync("this sentence are wrote bad and need fix");
        Assert.False(string.IsNullOrWhiteSpace(rephrased));

        // The agentic path must yield actions live. The model is nondeterministic, so retry a few
        // times and require at least one good plan — then every returned action must be well-formed.
        CacheNote.Core.Ai.AiPlan plan = new();
        for (var attempt = 0; attempt < 3 && plan.Actions.Count == 0; attempt++)
            plan = await _svc.PlanAsync("Plan a small birthday party: make a checklist and a task.");
        Assert.NotEmpty(plan.Actions);
        Assert.All(plan.Actions, a => Assert.False(string.IsNullOrWhiteSpace(a.Action)));
    }

    [Fact]
    public async Task Empty_Input_Degrades_Gracefully()
    {
        if (!_enabled) return;

        // Empty input must not hang or crash with an unexpected exception type. Either a (possibly
        // empty) string back, or the handled InvalidOperationException from GeminiClient, is fine.
        var ex = await Record.ExceptionAsync(() => _svc.SummarizeAsync(""));
        Assert.True(ex is null or InvalidOperationException, $"unexpected: {ex?.GetType().Name}");
    }

    [Fact]
    public async Task Oversized_Input_Is_Handled_Not_Crashing()
    {
        if (!_enabled) return;

        var huge = new string('x', 200_000);   // overflow the model's input budget → expect a clean 4xx
        var ex = await Record.ExceptionAsync(() => _svc.SummarizeAsync(huge));
        Assert.True(ex is null or InvalidOperationException, $"unexpected: {ex?.GetType().Name}");
    }

    private static string SolutionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CacheNote.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
