using StickyDesk.Core.Models;

namespace StickyDesk.Core.Services;

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

        var occ = start;
        var guard = 0;
        while (occ.Date < windowStart.Date && guard++ < 20000)
            occ = Step(occ, recurrence);
        while (occ.Date <= windowEnd.Date && guard++ < 20000)
        {
            if (occ.Date >= windowStart.Date)
                yield return occ;
            occ = Step(occ, recurrence);
        }
    }

    /// <summary>The first occurrence at or after <paramref name="after"/> (UTC in, UTC out).</summary>
    public static DateTime NextStartUtc(DateTime startUtc, string recurrence, DateTime after)
    {
        if (recurrence == EventRecurrence.None)
            return startUtc;
        var occ = startUtc;
        var guard = 0;
        while (occ < after && guard++ < 200000)
            occ = Step(occ, recurrence);
        return occ;
    }

    private static DateTime Step(DateTime d, string recurrence) => recurrence switch
    {
        EventRecurrence.Daily => d.AddDays(1),
        EventRecurrence.Weekly => d.AddDays(7),
        EventRecurrence.Monthly => d.AddMonths(1),
        EventRecurrence.Yearly => d.AddYears(1),
        _ => d.AddYears(1000),
    };
}
