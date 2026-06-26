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
    public void ParsePlan_ReadsReplyAndActions()
    {
        var raw = "```json\n{\"reply\":\"Sure!\",\"actions\":[{\"action\":\"create_reminder\",\"title\":\"Call mom\",\"date\":\"2026-07-01\"}]}\n```";
        var plan = AiAssistService.ParsePlan(raw);
        Assert.Equal("Sure!", plan.Reply);
        Assert.Single(plan.Actions);
        Assert.Equal("create_reminder", plan.Actions[0].Action);
    }

    [Fact]
    public void ParsePlan_ReadsSnakeCaseActionFieldsFromSchema()
    {
        var raw = """
        {
          "reply": "Done",
          "actions": [
            {
              "action": "create_event",
              "title": "Launch review",
              "meeting_url": "https://meet.example/launch",
              "alert_minutes": 15,
              "title_color_hex": "#FFFFFF"
            }
          ]
        }
        """;

        var action = Assert.Single(AiAssistService.ParsePlan(raw).Actions);
        Assert.Equal("https://meet.example/launch", action.MeetingUrl);
        Assert.Equal(15, action.AlertMinutes);
        Assert.Equal("#FFFFFF", action.TitleColorHex);
    }

    [Fact]
    public void Apply_CreatesReminder_Event_And_FavoriteNote()
    {
        var notes = new NoteRepository(_factory);
        var reminderRepo = new ReminderRepository(_factory);
        var reminders = new ReminderService(reminderRepo);
        var events = new EventService(new EventRepository(_factory), reminderRepo);
        var executor = new AiActionExecutor(notes, new ChecklistRepository(_factory),
            new TaskService(new TaskRepository(_factory)), new TagService(new TagRepository(_factory)),
            events, reminders);

        var actions = new List<AiAction>
        {
            new() { Action = AiActionKinds.CreateNote, Title = "Pinned idea", Body = "Visible note body", Favorite = true },
            new() { Action = AiActionKinds.CreateReminder, Title = "Standup", Date = "2026-07-01", Time = "09:00", Repeat = "daily" },
            new() { Action = AiActionKinds.CreateEvent, Title = "Frida's birthday", Date = "2026-06-25", Kind = "birthday", AlertMinutes = 0 },
        };

        executor.Apply(actions, currentNoteId: null);

        Assert.Contains(notes.GetAllActive(), n => n.Favorite && n.Title == "Pinned idea" && n.ContentRtf is { Length: > 0 });
        // The reminder lands on the reminder list AND a companion event on the calendar; the birthday
        // event (with an alert) also creates a linked reminder → at least 2 reminders, ≥ 2 events.
        Assert.True(reminders.GetAll().Count >= 2);
        Assert.True(events.GetAll().Count >= 2);
        Assert.Contains(events.GetAll(), e => e.Kind == "birthday" && e.Recurrence == "yearly");
    }

    [Fact]
    public void Apply_CurrentNoteActions_UpdateBodyFlagsAndAppend()
    {
        var notes = new NoteRepository(_factory);
        var reminderRepo = new ReminderRepository(_factory);
        var executor = new AiActionExecutor(notes, new ChecklistRepository(_factory),
            new TaskService(new TaskRepository(_factory)), new TagService(new TagRepository(_factory)),
            new EventService(new EventRepository(_factory), reminderRepo), new ReminderService(reminderRepo));

        var noteId = notes.Insert(new CacheNote.Core.Models.Note
        {
            Title = "Original",
            ContentPlain = "Old body",
        });

        executor.Apply([
            new() { Action = AiActionKinds.UpdateCurrentNote, Title = "Reworked", Body = "New body", Favorite = true, Pinned = true, TitleColorHex = "#FFFFFF" },
            new() { Action = AiActionKinds.AppendToCurrentNote, Body = "Follow-up line" },
        ], currentNoteId: noteId);

        var note = notes.GetById(noteId)!;
        Assert.Equal("Reworked", note.Title);
        Assert.Contains("New body", note.ContentPlain);
        Assert.Contains("Follow-up line", note.ContentPlain);
        Assert.NotNull(note.ContentRtf);
        Assert.True(note.Favorite);
        Assert.True(note.Pinned);
        Assert.Equal("#FFFFFF", note.TitleColorHex);
    }

    [Fact]
    public void Apply_ExposesCreatedNoteId_ForExactNavigation()
    {
        var notes = new NoteRepository(_factory);
        var reminderRepo = new ReminderRepository(_factory);
        var executor = new AiActionExecutor(notes, new ChecklistRepository(_factory),
            new TaskService(new TaskRepository(_factory)), new TagService(new TagRepository(_factory)),
            new EventService(new EventRepository(_factory), reminderRepo), new ReminderService(reminderRepo));

        // Two notes share a title — title lookup would be ambiguous; the id must be exact.
        notes.Insert(new CacheNote.Core.Models.Note { Title = "Dup", ContentPlain = "first" });
        executor.Apply([new() { Action = AiActionKinds.CreateNote, Title = "Dup", Body = "second" }], currentNoteId: null);

        Assert.NotNull(executor.LastCreatedNoteId);
        var created = notes.GetById(executor.LastCreatedNoteId!.Value)!;
        Assert.Equal("second", created.ContentPlain);

        // A follow-up apply that creates nothing must clear it.
        executor.Apply([new() { Action = AiActionKinds.AddTag, Name = "x" }], currentNoteId: created.Id);
        Assert.Null(executor.LastCreatedNoteId);
    }

    [Fact]
    public void IsDestructive_OnlyForArchiveOrDeleteState()
    {
        Assert.True(new AiAction { Action = AiActionKinds.SetCurrentNoteState, Deleted = true }.IsDestructive);
        Assert.True(new AiAction { Action = AiActionKinds.SetCurrentNoteState, Archived = true }.IsDestructive);
        Assert.False(new AiAction { Action = AiActionKinds.SetCurrentNoteState, Pinned = true }.IsDestructive);
        Assert.False(new AiAction { Action = AiActionKinds.CreateNote, Title = "x" }.IsDestructive);
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
            var reminderRepo = new ReminderRepository(_factory);
            var reminders = new ReminderService(reminderRepo);
            var events = new EventService(new EventRepository(_factory), reminderRepo);
            var executor = new AiActionExecutor(notes, checklist, tasks, tags, events, reminders);

            var cfg = new CloudConfig(_paths);
            Assert.Equal("fake", cfg.AiProvider);
            var svc = new AiAssistService(new GeminiClientFactory(cfg), executor);

            var plan = await svc.PlanAsync("organize my launch");
            Assert.NotEmpty(plan.Actions);

            svc.Apply(plan.Actions, currentNoteId: null);

            Assert.Contains(notes.GetAllActive(), n => n.Title.Contains("Plan", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(tasks.GetAll());
            Assert.Contains(tags.GetAll(), t => t.Name == "ai");
            Assert.NotEmpty(reminders.GetAll());   // the fake plan now includes a reminder + event
            Assert.NotEmpty(events.GetAll());
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
