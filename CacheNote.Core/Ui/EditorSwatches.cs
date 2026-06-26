namespace CacheNote.Core.Ui;

/// <summary>
/// Font-colour swatches offered in the note editor. CacheNote is dark-mode first, so black is
/// intentionally never offered as a text colour; "Default" and white cover normal text. Pure logic so it
/// can be unit-tested (the UI binds these into the swatch grid).
/// </summary>
public static class EditorSwatches
{
    public const string Auto = "auto";
    public const string White = "#FFFFFF";

    public static readonly (string Label, string Hex)[] All =
    [
        ("Default", Auto), (White, White), ("#D4D4D8", "#D4D4D8"), ("#A1A1AA", "#A1A1AA"),
        ("#2563EB", "#2563EB"), ("#0EA5E9", "#0EA5E9"), ("#16A34A", "#16A34A"),
        ("#F59E0B", "#F59E0B"), ("#F97316", "#F97316"), ("#EF4444", "#EF4444"),
        ("#A78BFA", "#A78BFA"), ("#F472B6", "#F472B6"),
    ];

    /// <summary>Swatches visible for the given theme (white hidden only if light mode is revived).</summary>
    public static IReadOnlyList<(string Label, string Hex)> Visible(bool dark) =>
        All.Where(s => dark || s.Hex != White).ToList();
}
