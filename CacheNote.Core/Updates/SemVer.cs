using System.Globalization;

namespace CacheNote.Core.Updates;

/// <summary>Tiny semantic-version comparison for the auto-updater (major.minor.patch, optional 'v', ignores pre-release tags).</summary>
public static class SemVer
{
    public static bool TryParse(string? s, out (int Major, int Minor, int Patch) version)
    {
        version = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+']);  // drop pre-release AND build metadata ("1.2.3+sha")
        if (cut >= 0)
            s = s[..cut];

        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        // A present-but-non-numeric component is a parse FAILURE, not 0 — coercing to 0 made
        // "1.2.3+sha" read as (1,2,0) (missed updates) and let garbage parse as (0,0,0).
        var nums = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length)
                continue;   // missing component ("1.2") defaults to 0
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i]))
                return false;
        }
        version = (nums[0], nums[1], nums[2]);
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
