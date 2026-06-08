namespace CacheNote.Core.Data.Migrations;

/// <summary>
/// Schema version 2 (V2 rerun) — a real calendar. Adds an <c>events</c> table for
/// appointments / meetings / birthdays with location, a meeting link (Join), all-day,
/// recurrence, a colour category, and a per-event alert that fires through the existing
/// reminder/toast engine (linked via reminder_id).
/// </summary>
internal static class SchemaV2
{
    public const string Sql = """
    CREATE TABLE events (
        id            INTEGER PRIMARY KEY AUTOINCREMENT,
        title         TEXT    NOT NULL DEFAULT '',
        location      TEXT,
        notes         TEXT,
        start_utc     TEXT    NOT NULL,
        end_utc       TEXT,
        all_day       INTEGER NOT NULL DEFAULT 0,
        kind          TEXT    NOT NULL DEFAULT 'event' CHECK(kind IN ('event','meeting','appointment','birthday')),
        color_hex     TEXT    NOT NULL DEFAULT '#2563EB',
        recurrence    TEXT    NOT NULL DEFAULT 'none' CHECK(recurrence IN ('none','daily','weekly','monthly','yearly')),
        meeting_url   TEXT,
        alert_minutes INTEGER,
        reminder_id   INTEGER REFERENCES reminders(id) ON DELETE SET NULL,
        created_utc   TEXT    NOT NULL,
        updated_utc   TEXT    NOT NULL
    );
    CREATE INDEX idx_events_start ON events(start_utc);
    """;
}
