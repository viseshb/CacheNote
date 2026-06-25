namespace CacheNote.Core.Ai;

/// <summary>The agentic actions the AI may propose (validated, previewed, then applied via repos).</summary>
public static class AiActionKinds
{
    public const string CreateNote = "create_note";
    public const string UpdateCurrentNote = "update_current_note";
    public const string AppendToCurrentNote = "append_to_current_note";
    public const string SetCurrentNoteState = "set_current_note_state";
    public const string AddChecklist = "add_checklist";
    public const string CreateTask = "create_task";
    public const string AddTag = "add_tag";
    public const string CreateReminder = "create_reminder";
    public const string CreateEvent = "create_event";
}

/// <summary>One proposed action from the AI's structured JSON output.</summary>
public sealed class AiAction
{
    public string Action { get; set; } = "";
    public string? Title { get; set; }
    public string? Body { get; set; }
    public List<string>? Items { get; set; }
    public string? Due { get; set; }        // task due — ISO-8601 or natural; parsed best-effort
    public string? Priority { get; set; }    // low | medium | high
    public string? Name { get; set; }        // tag name
    public bool? Favorite { get; set; }      // mark a created note as favorite
    public bool? Pinned { get; set; }
    public bool? Archived { get; set; }
    public bool? Deleted { get; set; }
    public string? TitleColorHex { get; set; }

    // Reminder / calendar event fields.
    public string? Date { get; set; }        // YYYY-MM-DD
    public string? Time { get; set; }        // HH:MM (24h); omitted = all-day / default
    public string? Repeat { get; set; }      // reminder: once|daily|weekly|monthly
    public string? Recurrence { get; set; }  // event: none|daily|weekly|monthly|yearly
    public string? Kind { get; set; }        // event|birthday|meeting|appointment
    public string? Location { get; set; }
    public string? MeetingUrl { get; set; }
    public int? AlertMinutes { get; set; }   // event alert: minutes before start (fires a reminder/toast)

    /// <summary>A human-readable one-line description for the preview.</summary>
    public string Describe() => Action switch
    {
        AiActionKinds.CreateNote => $"Create note: \"{Title}\"" + (Favorite == true ? " ★" : ""),
        AiActionKinds.UpdateCurrentNote => $"Update current note" + (string.IsNullOrWhiteSpace(Title) ? "" : $": \"{Title}\""),
        AiActionKinds.AppendToCurrentNote => "Append to current note",
        AiActionKinds.SetCurrentNoteState => "Update current note settings",
        AiActionKinds.AddChecklist => $"Add checklist ({Items?.Count ?? 0} items)",
        AiActionKinds.CreateTask => $"Create task: \"{Title}\"" + (Priority is null ? "" : $" [{Priority}]") + DueSuffix(),
        AiActionKinds.AddTag => $"Add tag: #{Name}",
        AiActionKinds.CreateReminder => $"Reminder: \"{Title}\"" + WhenSuffix() + RepeatSuffix(Repeat),
        AiActionKinds.CreateEvent => $"Calendar {Kind ?? "event"}: \"{Title}\"" + WhenSuffix() + RepeatSuffix(Recurrence) + AlertSuffix(),
        _ => $"{Action}",
    };

    private string DueSuffix() => string.IsNullOrWhiteSpace(Due) ? "" : $" (due {Due})";
    private string WhenSuffix() => string.IsNullOrWhiteSpace(Date) ? "" : $" on {Date}" + (string.IsNullOrWhiteSpace(Time) ? "" : $" at {Time}");
    private static string RepeatSuffix(string? r) => string.IsNullOrWhiteSpace(r) || r is "once" or "none" ? "" : $", repeats {r}";
    private string AlertSuffix() => AlertMinutes is null ? "" : AlertMinutes == 0 ? " (alert at start)" : $" (alert {AlertMinutes}m before)";
}

/// <summary>
/// The assistant's conversational turn: a natural-language reply plus the structured actions to
/// preview/apply. <see cref="Reply"/> may ask a clarifying question while still proposing a
/// sensible default in <see cref="Actions"/>, so the user can just Apply or refine by typing.
/// </summary>
public sealed class AiPlan
{
    public string Reply { get; set; } = "";
    public List<AiAction> Actions { get; set; } = new();
}
