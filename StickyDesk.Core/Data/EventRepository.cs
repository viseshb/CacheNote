using System.Globalization;
using Dapper;
using StickyDesk.Core.Models;

namespace StickyDesk.Core.Data;

public interface IEventRepository
{
    IReadOnlyList<CalendarEvent> GetAll();
    CalendarEvent? GetById(long id);
    long Insert(CalendarEvent e);
    void Update(CalendarEvent e);
    void Delete(long id);
    void SetReminderId(long eventId, long? reminderId);
}

public sealed class EventRepository : IEventRepository
{
    private readonly IDbConnectionFactory _factory;

    public EventRepository(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<CalendarEvent> GetAll()
    {
        using var conn = _factory.Create();
        return conn.Query<CalendarEvent>("SELECT * FROM events ORDER BY start_utc;").AsList();
    }

    public CalendarEvent? GetById(long id)
    {
        using var conn = _factory.Create();
        return conn.QuerySingleOrDefault<CalendarEvent>("SELECT * FROM events WHERE id = @id;", new { id });
    }

    public long Insert(CalendarEvent e)
    {
        using var conn = _factory.Create();
        var now = DateTime.UtcNow;
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO events(title, location, notes, start_utc, end_utc, all_day, kind, color_hex,
                               recurrence, meeting_url, alert_minutes, reminder_id, created_utc, updated_utc)
            VALUES (@Title, @Location, @Notes, @Start, @End, @AllDay, @Kind, @ColorHex,
                    @Recurrence, @MeetingUrl, @AlertMinutes, @ReminderId, @Created, @Updated);
            SELECT last_insert_rowid();
            """,
            new
            {
                e.Title,
                e.Location,
                e.Notes,
                Start = Iso(e.StartUtc),
                End = e.EndUtc.HasValue ? Iso(e.EndUtc.Value) : null,
                AllDay = e.AllDay ? 1 : 0,
                Kind = EventKinds.All.Contains(e.Kind) ? e.Kind : EventKinds.Event,
                e.ColorHex,
                Recurrence = EventRecurrence.All.Contains(e.Recurrence) ? e.Recurrence : EventRecurrence.None,
                e.MeetingUrl,
                e.AlertMinutes,
                e.ReminderId,
                Created = Iso(e.CreatedUtc == default ? now : e.CreatedUtc),
                Updated = Iso(e.UpdatedUtc == default ? now : e.UpdatedUtc),
            });
    }

    public void Update(CalendarEvent e)
    {
        using var conn = _factory.Create();
        conn.Execute(
            """
            UPDATE events
            SET title = @Title, location = @Location, notes = @Notes, start_utc = @Start, end_utc = @End,
                all_day = @AllDay, kind = @Kind, color_hex = @ColorHex, recurrence = @Recurrence,
                meeting_url = @MeetingUrl, alert_minutes = @AlertMinutes, reminder_id = @ReminderId,
                updated_utc = @Updated
            WHERE id = @Id;
            """,
            new
            {
                e.Id,
                e.Title,
                e.Location,
                e.Notes,
                Start = Iso(e.StartUtc),
                End = e.EndUtc.HasValue ? Iso(e.EndUtc.Value) : null,
                AllDay = e.AllDay ? 1 : 0,
                Kind = EventKinds.All.Contains(e.Kind) ? e.Kind : EventKinds.Event,
                e.ColorHex,
                Recurrence = EventRecurrence.All.Contains(e.Recurrence) ? e.Recurrence : EventRecurrence.None,
                e.MeetingUrl,
                e.AlertMinutes,
                e.ReminderId,
                Updated = Iso(DateTime.UtcNow),
            });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM events WHERE id = @id;", new { id });
    }

    public void SetReminderId(long eventId, long? reminderId)
    {
        using var conn = _factory.Create();
        conn.Execute("UPDATE events SET reminder_id = @reminderId WHERE id = @eventId;", new { eventId, reminderId });
    }

    private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
