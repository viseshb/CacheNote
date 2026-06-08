namespace CacheNote.Core.Ai;

/// <summary>The agentic actions the AI may propose (validated, previewed, then applied via repos).</summary>
public static class AiActionKinds
{
    public const string CreateNote = "create_note";
    public const string AddChecklist = "add_checklist";
    public const string CreateTask = "create_task";
    public const string AddTag = "add_tag";
}

/// <summary>One proposed action from the AI's structured JSON output.</summary>
public sealed class AiAction
{
    public string Action { get; set; } = "";
    public string? Title { get; set; }
    public string? Body { get; set; }
    public List<string>? Items { get; set; }
    public string? Due { get; set; }       // ISO-8601 or natural; parsed best-effort
    public string? Priority { get; set; }  // low | medium | high
    public string? Name { get; set; }      // tag name

    /// <summary>A human-readable one-line description for the preview dialog.</summary>
    public string Describe() => Action switch
    {
        AiActionKinds.CreateNote => $"Create note: \"{Title}\"",
        AiActionKinds.AddChecklist => $"Add checklist ({Items?.Count ?? 0} items)",
        AiActionKinds.CreateTask => $"Create task: \"{Title}\"" + (Priority is null ? "" : $" [{Priority}]"),
        AiActionKinds.AddTag => $"Add tag: #{Name}",
        _ => $"{Action}",
    };
}
