namespace CacheNote.Core.Data.Migrations;

/// <summary>
/// Schema version 4 — Google Calendar two-way sync. Events gain a link to their Google
/// counterpart (google_id) plus the Google "updated" stamp recorded at last sync (the
/// conflict baseline: a side that moved past this stamp has changes to sync). Local deletes
/// of linked events are tombstoned in google_deletes so the next sync can delete remotely.
/// </summary>
internal static class SchemaV4
{
    public const string Sql = """
    ALTER TABLE events ADD COLUMN google_id TEXT;
    ALTER TABLE events ADD COLUMN google_updated_utc TEXT;
    CREATE UNIQUE INDEX idx_events_google_id ON events(google_id) WHERE google_id IS NOT NULL;
    CREATE TABLE google_deletes (
        google_id TEXT PRIMARY KEY
    );
    """;
}
