using CacheNote.Core.Models;

namespace CacheNote.Core.Services;

/// <summary>Expands a recurring event into concrete occurrences. Bounded (guards against runaway loops).</summary>
public static class Recurrence
{
    /// <summary>Occurrence start times (same kind as <paramref name="start"/>) that fall within [windowStart, windowEnd] by date.</summary>
    public static IEnumerable<DateTime> Occurrences(DateTime start, string recurrence, DateTime windowStart, DateTime windowEnd)
    {
        if (recurrence == EventRecurrence.None)
        {
            if (start.Date >= windowStart.Date && start.Date <= windowEnd.Date)
                yield return start;
            yield break;
        }

        // Occurrence n is always computed from the ORIGINAL start (start.AddMonths(n)), never by
        // stepping the previous occurrence. Iterative stepping permanently loses the anchor day:
        // monthly from Jan 31 would clamp to Feb 28 and then stay on the 28th forever, and a
        // Feb 29 birthday would never return to Feb 29 on later leap years.
        var n = FirstIndexOnOrAfter(start, recurrence, windowStart.Date);
        var guard = 0;
        while (guard++ < 20000)
        {
            var occ = Nth(start, recurrence, n);
            if (occ.Date > windowEnd.Date)
                yield break;
            if (occ.Date >= windowStart.Date)
                yield return occ;
            n++;
        }
    }

    /// <summary>
    /// The first occurrence at or after <paramref name="after"/> (UTC in, UTC out).
    /// Occurrences are stepped in LOCAL wall-clock time, then converted back: a daily 9:00 AM
    /// event must stay 9:00 AM local across a DST change — stepping the stored UTC instant
    /// shifted every recurring event by an hour twice a year.
    /// </summary>
    public static DateTime NextStartUtc(DateTime startUtc, string recurrence, DateTime after)
    {
        if (recurrence == EventRecurrence.None)
            return startUtc;
        var startLocal = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc).ToLocalTime();
        var afterLocal = DateTime.SpecifyKind(after, DateTimeKind.Utc).ToLocalTime();
        var n = FirstIndexOnOrAfter(startLocal, recurrence, afterLocal);
        var guard = 0;
        while (Nth(startLocal, recurrence, n) < afterLocal && guard++ < 1000)
            n++;
        return Nth(startLocal, recurrence, n).ToUniversalTime();
    }

    /// <summary>The n-th occurrence, anchored to the original start.</summary>
    private static DateTime Nth(DateTime start, string recurrence, int n) => recurrence switch
    {
        EventRecurrence.Daily => start.AddDays(n),
        EventRecurrence.Weekly => start.AddDays(7L * n),
        EventRecurrence.Monthly => start.AddMonths(n),
        EventRecurrence.Yearly => start.AddYears(n),
        _ => n == 0 ? start : DateTime.MaxValue,
    };

    /// <summary>Cheap lower-bound estimate of the first occurrence index landing on/after <paramref name="target"/> (may undershoot, never overshoots).</summary>
    private static int FirstIndexOnOrAfter(DateTime start, string recurrence, DateTime target)
    {
        if (target <= start)
            return 0;
        var span = target - start;
        var n = recurrence switch
        {
            EventRecurrence.Daily => (long)span.TotalDays - 1,
            EventRecurrence.Weekly => (long)(span.TotalDays / 7) - 1,
            EventRecurrence.Monthly => (long)(span.TotalDays / 32) - 1,
            EventRecurrence.Yearly => (long)(span.TotalDays / 367) - 1,
            _ => 0L,
        };
        return (int)Math.Max(0, n);
    }
}
