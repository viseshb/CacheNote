namespace CacheNote.Core.Data.Migrations;

/// <summary>
/// Schema version 5 — per-note title color. The font-color tool can color the note title
/// (a plain TextBox, so one color for the whole title); shown in the editor header and the
/// notes list. NULL = follow the theme's default text color.
/// </summary>
internal static class SchemaV5
{
    public const string Sql = """
    ALTER TABLE notes ADD COLUMN title_color_hex TEXT;
    """;
}
