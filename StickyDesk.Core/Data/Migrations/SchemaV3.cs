namespace StickyDesk.Core.Data.Migrations;

/// <summary>
/// Schema version 3 (V2 rerun) — Markdown blocks. The editor's {} tool inserts a Markdown block
/// into the current note (appended); each block stores raw Markdown (great for code blocks) and is
/// rendered/previewed in the editor. Parallels checklist_items.
/// </summary>
internal static class SchemaV3
{
    public const string Sql = """
    CREATE TABLE note_md_blocks (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        note_id     INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
        content     TEXT    NOT NULL DEFAULT '',
        sort_order  INTEGER NOT NULL DEFAULT 0,
        created_utc TEXT    NOT NULL
    );
    CREATE INDEX idx_md_blocks_note ON note_md_blocks(note_id, sort_order);
    """;
}
