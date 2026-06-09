using CacheNote.Core.Data;
using CacheNote.Core.Data.Migrations;
using CacheNote.Core.DependencyInjection;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Models;
using CacheNote.Core.Services;

namespace CacheNote.Tests;

/// <summary>
/// Deterministic Calendar module data path: event create / read / update / delete, plus the
/// alert → linked-reminder wiring (the templated calendar rows are exercised live by
/// V2_CalendarSmoke; this proves the CRUD + alert logic without UIA flakiness).
/// </summary>
public sealed class EventServiceTests : IDisposable
{
    private readonly string _root;
    private readonly EventService _events;
    private readonly ReminderRepository _reminders;

    public EventServiceTests()
    {
        CoreServiceCollectionExtensions.ConfigureDapper();
        _root = Path.Combine(Path.GetTempPath(), "CacheNote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(_root);
        var factory = new SqliteConnectionFactory(paths);
        new MigrationRunner(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<MigrationRunner>.Instance).Run();
        _reminders = new ReminderRepository(factory);
        _events = new EventService(new EventRepository(factory), _reminders);
    }

    [Fact]
    public void Event_Create_Read_Update_Delete()
    {
        var id = _events.Save(new CalendarEvent
        {
            Title = "Team Sync",
            StartUtc = DateTime.UtcNow.AddHours(2),
            EndUtc = DateTime.UtcNow.AddHours(3),
            Kind = EventKinds.Meeting,
            MeetingUrl = "https://meet.google.com/abc-defg-hij",
        });
        Assert.NotEqual(0, id);

        var read = _events.GetById(id);
        Assert.NotNull(read);
        Assert.Equal("Team Sync", read!.Title);
        Assert.Equal(EventKinds.Meeting, read.Kind);
        Assert.Equal("https://meet.google.com/abc-defg-hij", read.MeetingUrl);

        // Edit it.
        read.Title = "Team Sync (moved)";
        read.StartUtc = read.StartUtc.AddDays(1);
        _events.Save(read);
        Assert.Equal("Team Sync (moved)", _events.GetById(id)!.Title);
        Assert.Single(_events.GetAll());

        // Delete it.
        _events.Delete(id);
        Assert.Null(_events.GetById(id));
        Assert.Empty(_events.GetAll());
    }

    [Fact]
    public void Event_WithAlert_CreatesLinkedReminder_RemovedOnDelete()
    {
        var id = _events.Save(new CalendarEvent
        {
            Title = "Dentist",
            StartUtc = DateTime.UtcNow.AddHours(5),
            Kind = EventKinds.Appointment,
            AlertMinutes = 30,
        });

        var ev = _events.GetById(id)!;
        Assert.NotNull(ev.ReminderId);
        Assert.NotNull(_reminders.GetById(ev.ReminderId!.Value));

        _events.Delete(id);
        Assert.Null(_reminders.GetById(ev.ReminderId!.Value)); // alert reminder removed with the event
    }

    [Fact]
    public void Event_RemovingAlert_DeletesLinkedReminder()
    {
        var id = _events.Save(new CalendarEvent { Title = "Standup", StartUtc = DateTime.UtcNow.AddHours(1), AlertMinutes = 10 });
        var rid = _events.GetById(id)!.ReminderId;
        Assert.NotNull(rid);

        var ev = _events.GetById(id)!;
        ev.AlertMinutes = null;
        _events.Save(ev);

        Assert.Null(_events.GetById(id)!.ReminderId);
        Assert.Null(_reminders.GetById(rid!.Value));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
