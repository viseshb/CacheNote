using CacheNote.Core.Data;
using CacheNote.Core.Data.Migrations;
using CacheNote.Core.DependencyInjection;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Models;
using CacheNote.Core.Services;

namespace CacheNote.Tests;

public sealed class ReminderServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ReminderService _service;
    private readonly ReminderRepository _repo;

    public ReminderServiceTests()
    {
        CoreServiceCollectionExtensions.ConfigureDapper(); // registers the UTC DateTime handler + name map
        _root = Path.Combine(Path.GetTempPath(), "CacheNote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(_root);
        var factory = new SqliteConnectionFactory(paths);
        new MigrationRunner(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance).Run();
        _repo = new ReminderRepository(factory);
        _service = new ReminderService(_repo);
    }

    // ---- pure next-fire math ----

    [Theory]
    [InlineData(RepeatKinds.Daily, 1)]
    [InlineData(RepeatKinds.Weekly, 7)]
    public void AdvancePastNow_RollsForwardByInterval_PastNow(string repeat, int stepDays)
    {
        var fire = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        var next = ReminderMath.AdvancePastNow(fire, repeat, now);

        Assert.True(next > now);
        Assert.Equal(0, ((next - fire).Days) % stepDays); // landed on an exact interval boundary
    }

    [Fact]
    public void AdvancePastNow_Once_NeverMoves()
    {
        var fire = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(fire, ReminderMath.AdvancePastNow(fire, RepeatKinds.Once, now));
    }

    [Fact]
    public void AdvancePastNow_Monthly_AdvancesAtLeastOneMonth()
    {
        var fire = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 20, 8, 0, 0, DateTimeKind.Utc);
        var next = ReminderMath.AdvancePastNow(fire, RepeatKinds.Monthly, now);
        Assert.Equal(new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc), next);
    }

    // ---- service round-trips through SQLite ----

    [Fact]
    public void DueOnceReminder_Fires_ThenIsDismissed()
    {
        var now = DateTime.UtcNow;
        _service.Create(noteId: null, "drink water", now.AddMinutes(-1), RepeatKinds.Once);

        var fired = _service.GetDueAndAdvance(now);

        Assert.Single(fired);
        Assert.Equal("drink water", fired[0].Message);
        // Already-fired one-shot is gone from the due set and marked dismissed.
        Assert.Empty(_service.GetDueAndAdvance(now));
        Assert.True(_repo.GetById(fired[0].Id)!.IsDismissed);
    }

    [Fact]
    public void DueDailyReminder_Fires_ThenRescheduledForFuture()
    {
        var now = DateTime.UtcNow;
        var id = _service.Create(noteId: null, "standup", now.AddMinutes(-1), RepeatKinds.Daily);

        var fired = _service.GetDueAndAdvance(now);

        Assert.Single(fired);
        var after = _repo.GetById(id)!;
        Assert.False(after.IsDismissed);
        Assert.True(after.NextFireUtc > now);                 // rolled forward
        Assert.Empty(_service.GetDueAndAdvance(now));         // not due again now
    }

    [Fact]
    public void Snooze_ReactivatesAndRefiresAfterDelay()
    {
        var now = DateTime.UtcNow;
        var id = _service.Create(noteId: null, "call mom", now.AddMinutes(-1), RepeatKinds.Once);
        _service.GetDueAndAdvance(now);                        // fires + dismisses

        _service.Snooze(id, minutes: 5, now);

        Assert.Empty(_service.GetDueAndAdvance(now));          // not due during the snooze
        Assert.Single(_service.GetDueAndAdvance(now.AddMinutes(6))); // due again after snooze elapses
    }

    [Fact]
    public void Complete_RemovesFromDue()
    {
        var now = DateTime.UtcNow;
        var id = _service.Create(noteId: null, "ship it", now.AddMinutes(-1), RepeatKinds.Daily);
        _service.Complete(id);
        Assert.Empty(_service.GetDueAndAdvance(now));
        Assert.True(_repo.GetById(id)!.IsDismissed);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
