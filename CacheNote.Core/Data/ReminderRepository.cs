using System.Globalization;
using Dapper;
using CacheNote.Core.Models;

namespace CacheNote.Core.Data;

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
            """,
            new
            {
                reminder.NoteId,
                reminder.TaskId,
                RemindUtc = Iso(reminder.RemindUtc),
                reminder.Message,
                reminder.Repeat,
                NextFireUtc = IsoOrNull(reminder.NextFireUtc),
                SnoozeUntilUtc = IsoOrNull(reminder.SnoozeUntilUtc),
                reminder.IsDismissed,
            });
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
             """, new { nowUtc = Iso(nowUtc) }).AsList();
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
            """, new { id, nextFireUtc = IsoOrNull(nextFireUtc), snoozeUntilUtc = IsoOrNull(snoozeUntilUtc), dismissed });
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
            """, new { id, message, remindUtc = Iso(remindUtc), repeat, nextFireUtc = Iso(nextFireUtc) });
    }

    public void Delete(long id)
    {
        using var conn = _factory.Create();
        conn.Execute("DELETE FROM reminders WHERE id = @id;", new { id });
    }

    // Dapper's built-in DateTime binding bypasses our UtcDateTimeHandler for PARAMETERS
    // (its primitive type map wins), and Microsoft.Data.Sqlite would then store a
    // space-separated string with no 'Z'. Pre-format to ISO-8601 UTC like every other
    // repository so storage format and SQL string comparisons stay consistent.
    private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? IsoOrNull(DateTime? dt) => dt.HasValue ? Iso(dt.Value) : null;
}
