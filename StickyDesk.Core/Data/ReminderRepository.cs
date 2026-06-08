using Dapper;
using StickyDesk.Core.Models;

namespace StickyDesk.Core.Data;

public interface IReminderRepository
{
    long Insert(Reminder reminder);
    Reminder? GetById(long id);

    /// <summary>Reminders firing at or before <paramref name="nowUtc"/> that are not dismissed.</summary>
    IReadOnlyList<Reminder> GetDue(DateTime nowUtc);

    /// <summary>All reminders, soonest first, dismissed ones last (for the Reminders page).</summary>
    IReadOnlyList<Reminder> GetAll();

    void UpdateSchedule(long id, DateTime? nextFireUtc, DateTime? snoozeUntilUtc, bool dismissed);
    void UpdateDetails(long id, string? message, DateTime remindUtc, string repeat, DateTime nextFireUtc);
    void Delete(long id);
}

public sealed class ReminderRepository : IReminderRepository
{
    // Effective fire time = snooze (if any), else next fire, else the original time.
    private const string Effective = "COALESCE(snooze_until_utc, next_fire_utc, remind_utc)";

    private readonly IDbConnectionFactory _factory;

    public ReminderRepository(IDbConnectionFactory factory) => _factory = factory;

    public long Insert(Reminder reminder)
    {
        using var conn = _factory.Create();
        return conn.ExecuteScalar<long>(
            """
            INSERT INTO reminders(note_id, task_id, remind_utc, message, repeat, next_fire_utc, snooze_until_utc, is_dismissed)
            VALUES (@NoteId, @TaskId, @RemindUtc, @Message, @Repeat, @NextFireUtc, @SnoozeUntilUtc, @IsDismissed);
            SELECT last_insert_rowid();
            """, reminder);
    }

    public Reminder? GetById(long id)
    {
        using var conn = _factory.Create();
        return conn.QuerySingleOrDefault<Reminder>("SELECT * FROM reminders WHERE id = @id;", new { id });
    }

    public IReadOnlyList<Reminder> GetDue(DateTime nowUtc)
    {
        using var conn = _factory.Create();
        return conn.Query<Reminder>(
            $"""
             SELECT * FROM reminders
             WHERE is_dismissed = 0 AND {Effective} <= @nowUtc
             ORDER BY {Effective};
             """, new { nowUtc }).AsList();
    }

    public IReadOnlyList<Reminder> GetAll()
    {
        using var conn = _factory.Create();
        return conn.Query<Reminder>(
            $"SELECT * FROM reminders ORDER BY is_dismissed, {Effective};").AsList();
    }

    public void UpdateSchedule(long id, DateTime? nextFireUtc, DateTime? snoozeUntilUtc, bool dismissed)
    {
        using var conn = _factory.Create();
        conn.Execute(
            """
            UPDATE reminders
            SET next_fire_utc = @nextFireUtc, snooze_until_utc = @snoozeUntilUtc, is_dismissed = @dismissed
            WHERE id = @id;
            """, new { id, nextFireUtc, snoozeUntilUtc, dismissed });
    }

    public void UpdateDetails(long id, string? message, DateTime remindUtc, string repeat, DateTime nextFireUtc)
    {
        using var conn = _factory.Create();
        conn.Execute(
            """
            UPDATE reminders
            SET message = @message, remind_utc = @remindUtc, repeat = @repeat,
                next_fire_utc = @nextFireUtc, snooze_until_utc = NULL, is_dismissed = 0
            WHERE id = @id;
            """, new { id, message, remindUtc, repeat, nextFireUtc });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM reminders WHERE id = @id;", new { id });
    }
}
