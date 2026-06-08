namespace CacheNote.Core.Models;

/// <summary>Allowed event kinds (matches the DB CHECK constraint).</summary>
public static class EventKinds
{
    public const string Event = "event";
    public const string Meeting = "meeting";
    public const string Appointment = "appointment";
    public const string Birthday = "birthday";
    public static readonly string[] All = [Event, Meeting, Appointment, Birthday];
}

/// <summary>Allowed event recurrence cadences (matches the DB CHECK constraint).</summary>
public static class EventRecurrence
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public static readonly string[] All = [None, Daily, Weekly, Monthly, Yearly];
}

/// <summary>
/// A calendar event: appointment / meeting / birthday. All times UTC. May recur; may carry a
/// meeting link (Join) and an alert (minutes before start) routed through the reminder engine.
/// </summary>
public sealed class CalendarEvent
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public bool AllDay { get; set; }
    public string Kind { get; set; } = EventKinds.Event;
    public string ColorHex { get; set; } = "#2563EB";
    public string Recurrence { get; set; } = EventRecurrence.None;
    public string? MeetingUrl { get; set; }

    /// <summary>Minutes before start to fire an alert; null = no alert.</summary>
    public int? AlertMinutes { get; set; }

    /// <summary>The reminder row backing this event's alert (so it can be updated/deleted).</summary>
    public long? ReminderId { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
