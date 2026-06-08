using System.Globalization;

namespace StickyDesk.Core.Updates;

/// <summary>Tiny semantic-version comparison for the auto-updater (major.minor.patch, optional 'v', ignores pre-release tags).</summary>
public static class SemVer
{
    public static bool TryParse(string? s, out (int Major, int Minor, int Patch) version)
    {
        version = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim().TrimStart('v', 'V');
        var dash = s.IndexOf('-');           // drop pre-release / build suffix
        if (dash >= 0)
            s = s[..dash];

        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        int Get(int i) => i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        version = (Get(0), Get(1), Get(2));
        return true;
    }

    public static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        return a.Patch.CompareTo(b.Patch);
    }

    /// <summary>True if <paramref name="latest"/> is a strictly higher version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? latest, string? current)
        => TryParse(latest, out var l) && TryParse(current, out var c) && Compare(l, c) > 0;
}
