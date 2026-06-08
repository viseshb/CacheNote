namespace CacheNote.Core.Models;

/// <summary>The allowed reminder repeat cadences (matches the DB CHECK constraint).</summary>
public static class RepeatKinds
{
    public const string Once = "once";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly string[] All = [Once, Daily, Weekly, Monthly];
}

/// <summary>
/// A time-based reminder. May be attached to a note (or later a task) or stand alone.
/// All times are UTC. The reminder fires at <see cref="EffectiveFireUtc"/>: the snooze
/// time if snoozed, otherwise its next scheduled fire.
/// </summary>
public sealed class Reminder
{
    public long Id { get; set; }
    public long? NoteId { get; set; }
    public long? TaskId { get; set; }

    /// <summary>The originally requested time (kept for reference / editing).</summary>
    public DateTime RemindUtc { get; set; }

    public string? Message { get; set; }

    /// <summary>once | daily | weekly | monthly (see <see cref="RepeatKinds"/>).</summary>
    public string Repeat { get; set; } = RepeatKinds.Once;

    /// <summary>The next scheduled fire time (advances for repeating reminders).</summary>
    public DateTime? NextFireUtc { get; set; }

    /// <summary>When set and in the future, overrides <see cref="NextFireUtc"/> for the next fire.</summary>
    public DateTime? SnoozeUntilUtc { get; set; }

    public bool IsDismissed { get; set; }

    /// <summary>The time this reminder will actually fire next.</summary>
    public DateTime EffectiveFireUtc => SnoozeUntilUtc ?? NextFireUtc ?? RemindUtc;
}
