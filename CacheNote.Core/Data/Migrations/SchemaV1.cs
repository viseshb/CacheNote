namespace CacheNote.Core.Data.Migrations;

/// <summary>
/// Schema version 1 — the complete CacheNote data model. Built once via the
/// <see cref="MigrationRunner"/>. Designed normalized-but-pragmatic and
/// forward-compatible (e.g. notes.sync_status reserved for future cloud sync).
/// favorite/pinned are columns on notes (deliberate, approved deviation from
/// the PRD's separate "favorites" table — faster, simpler, no join).
/// </summary>
internal static class SchemaV1
{
    public const string Sql = """
    CREATE TABLE notes (
        id            INTEGER PRIMARY KEY AUTOINCREMENT,
        title         TEXT    NOT NULL DEFAULT '',
        content_rtf   BLOB,
        content_plain TEXT    NOT NULL DEFAULT '',
        pinned        INTEGER NOT NULL DEFAULT 0,
        favorite      INTEGER NOT NULL DEFAULT 0,
        is_archived   INTEGER NOT NULL DEFAULT 0,
        is_deleted    INTEGER NOT NULL DEFAULT 0,
        created_utc   TEXT    NOT NULL,
        updated_utc   TEXT    NOT NULL,
        sync_status   TEXT    NOT NULL DEFAULT 'local'
    );
    CREATE INDEX idx_notes_updated ON notes(updated_utc DESC);
    CREATE INDEX idx_notes_flags   ON notes(is_deleted, is_archived, pinned DESC, favorite DESC);

    CREATE TABLE checklist_items (
        id         INTEGER PRIMARY KEY AUTOINCREMENT,
        note_id    INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
        text       TEXT    NOT NULL DEFAULT '',
        is_done    INTEGER NOT NULL DEFAULT 0,
        sort_order INTEGER NOT NULL DEFAULT 0
    );
    CREATE INDEX idx_checklist_note ON checklist_items(note_id, sort_order);

    CREATE TABLE tasks (
        id           INTEGER PRIMARY KEY AUTOINCREMENT,
        note_id      INTEGER REFERENCES notes(id) ON DELETE CASCADE,
        title        TEXT    NOT NULL DEFAULT '',
        description  TEXT,
        due_utc      TEXT,
        priority     TEXT    NOT NULL DEFAULT 'medium' CHECK(priority IN ('low','medium','high')),
        is_completed INTEGER NOT NULL DEFAULT 0,
        created_utc  TEXT    NOT NULL,
        updated_utc  TEXT    NOT NULL
    );
    CREATE INDEX idx_tasks_note ON tasks(note_id);
    CREATE INDEX idx_tasks_due  ON tasks(due_utc);

    CREATE TABLE reminders (
        id               INTEGER PRIMARY KEY AUTOINCREMENT,
        note_id          INTEGER REFERENCES notes(id) ON DELETE CASCADE,
        task_id          INTEGER REFERENCES tasks(id) ON DELETE CASCADE,
        remind_utc       TEXT    NOT NULL,
        message          TEXT,
        repeat           TEXT    NOT NULL DEFAULT 'once' CHECK(repeat IN ('once','daily','weekly','monthly')),
        next_fire_utc    TEXT,
        snooze_until_utc TEXT,
        is_dismissed     INTEGER NOT NULL DEFAULT 0
    );
    CREATE INDEX idx_reminders_next ON reminders(next_fire_utc) WHERE is_dismissed = 0;

    CREATE TABLE tags (
        id        INTEGER PRIMARY KEY AUTOINCREMENT,
        name      TEXT    NOT NULL UNIQUE,
        color_hex TEXT    NOT NULL DEFAULT '#71717A'
    );

    CREATE TABLE note_tags (
        note_id INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
        tag_id  INTEGER NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
        PRIMARY KEY (note_id, tag_id)
    );

    CREATE TABLE attachments (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        note_id     INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
        filename    TEXT    NOT NULL,
        rel_path    TEXT    NOT NULL,
        mime        TEXT,
        size_bytes  INTEGER NOT NULL DEFAULT 0,
        created_utc TEXT    NOT NULL
    );
    CREATE INDEX idx_attachments_note ON attachments(note_id);

    CREATE TABLE settings (
        key         TEXT PRIMARY KEY,
        value       TEXT,
        updated_utc TEXT NOT NULL
    );

    -- Full-text search over notes (external-content FTS5, kept in sync by triggers).
    CREATE VIRTUAL TABLE notes_fts USING fts5(
        title,
        content_plain,
        content='notes',
        content_rowid='id'
    );

    CREATE TRIGGER notes_ai AFTER INSERT ON notes BEGIN
        INSERT INTO notes_fts(rowid, title, content_plain)
        VALUES (new.id, new.title, new.content_plain);
    END;

    CREATE TRIGGER notes_ad AFTER DELETE ON notes BEGIN
        INSERT INTO notes_fts(notes_fts, rowid, title, content_plain)
        VALUES ('delete', old.id, old.title, old.content_plain);
    END;

    CREATE TRIGGER notes_au AFTER UPDATE ON notes BEGIN
        INSERT INTO notes_fts(notes_fts, rowid, title, content_plain)
        VALUES ('delete', old.id, old.title, old.content_plain);
        INSERT INTO notes_fts(rowid, title, content_plain)
        VALUES (new.id, new.title, new.content_plain);
    END;
    """;
}
