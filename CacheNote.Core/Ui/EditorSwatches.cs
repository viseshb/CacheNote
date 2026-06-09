namespace CacheNote.Core.Ui;

/// <summary>
/// Font-colour swatches offered in the note editor, filtered per theme so the near-invisible
/// colour is never offered: black is dropped in dark mode, white in light mode. Pure logic so it
/// can be unit-tested (the UI binds these into the swatch grid).
/// </summary>
public static class EditorSwatches
{
    public const string Auto = "auto";
    public const string Black = "#18181B";
    public const string White = "#FFFFFF";

    public static readonly (string Label, string Hex)[] All =
    [
        ("Default", Auto), (Black, Black), ("#71717A", "#71717A"),
        ("#2563EB", "#2563EB"), ("#0EA5E9", "#0EA5E9"), ("#16A34A", "#16A34A"),
        ("#D97706", "#D97706"), ("#DC2626", "#DC2626"), ("#7C3AED", "#7C3AED"),
        (White, White),
    ];

    /// <summary>Swatches visible for the given theme (black hidden on dark, white hidden on light).</summary>
    public static IReadOnlyList<(string Label, string Hex)> Visible(bool dark) =>
        All.Where(s => !(dark && s.Hex == Black) && !(!dark && s.Hex == White)).ToList();
}
