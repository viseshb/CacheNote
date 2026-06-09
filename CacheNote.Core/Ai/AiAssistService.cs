using System.Text.Json;

namespace CacheNote.Core.Ai;

/// <summary>
/// High-level AI features: summarize a note, rephrase selected text, and "agentic" planning that
/// returns a structured action list for preview-then-apply. Creates the client per call so a
/// provider/key change (or the fake switch) is picked up without a restart.
/// </summary>
public sealed class AiAssistService
{
    private readonly IGeminiClientFactory _factory;
    private readonly AiActionExecutor _executor;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const string SummarySystem = "You are a concise note assistant. Summarize the user's note in 2-4 short sentences. Plain text only.";
    private const string RephraseSystem = "You rephrase the user's text to be clearer and well-written. Keep the meaning. Return only the rewritten text.";

    private const string AgentSystem =
        "You are CacheNote's friendly productivity assistant. The user can ask you to create notes, tasks, "
        + "reminders, calendar events, and favorites. Read the conversation (most recent message last) and respond.\n\n"
        + "Respond with ONLY a JSON object: {\"reply\":\"...\",\"actions\":[ ... ]} — no code fences, no prose outside the JSON.\n"
        + "ACT BY DEFAULT: the user wants things done, so almost always include the action(s) for your best interpretation. "
        + "The app applies your actions immediately, so write \"reply\" as a brief (1-2 sentence) past-tense confirmation of "
        + "what you added.\n"
        + "ONLY when the request is genuinely too vague to act on (e.g. \"remind me\" with no subject, or a dated item with no "
        + "date you can infer) return an EMPTY \"actions\" array and ask ONE short clarifying question in \"reply\". Do NOT ask "
        + "about optional details you can reasonably default (priority, exact time, recurrence) — just pick a sensible default "
        + "and mention it in the reply.\n\n"
        + "Action types for \"actions\":\n"
        + "- {\"action\":\"create_note\",\"title\":\"...\",\"body\":\"...\",\"favorite\":true|false}\n"
        + "- {\"action\":\"add_checklist\",\"items\":[\"...\"]}            (attaches to the note just created or the open note)\n"
        + "- {\"action\":\"create_task\",\"title\":\"...\",\"priority\":\"low|medium|high\",\"due\":\"YYYY-MM-DD\"}\n"
        + "- {\"action\":\"create_reminder\",\"title\":\"...\",\"date\":\"YYYY-MM-DD\",\"time\":\"HH:MM\",\"repeat\":\"once|daily|weekly|monthly\"}\n"
        + "- {\"action\":\"create_event\",\"title\":\"...\",\"date\":\"YYYY-MM-DD\",\"time\":\"HH:MM\",\"kind\":\"event|birthday|meeting|appointment\",\"recurrence\":\"none|daily|weekly|monthly|yearly\",\"location\":\"...\",\"meeting_url\":\"...\",\"alert_minutes\":N}\n"
        + "- {\"action\":\"add_tag\",\"name\":\"...\"}\n\n"
        + "Be smart:\n"
        + "- A reminder tied to a date/time is best created as a create_event WITH alert_minutes set: a calendar event with an "
        + "alert appears on the calendar AND fires a reminder/notification, so it covers BOTH (a reminder is just a calendar "
        + "event with an alert). Only use create_reminder for a quick nudge with no real calendar meaning.\n"
        + "- Birthdays: create_event kind=\"birthday\", recurrence=\"yearly\", all-day (omit time), alert_minutes 0.\n"
        + "- Meetings/appointments with a time: create_event with the matching kind; include meeting_url if the user gave a link.\n"
        + "- \"favorite\"/\"important\" note -> create_note with favorite=true.\n"
        + "- Resolve relative dates (\"tomorrow\", \"next Friday\", \"June 25th\") against the current date below; if a date has no "
        + "year, use the next upcoming occurrence.";

    private const string AgentSchema =
        "{\"reply\":\"string\",\"actions\":[{\"action\":\"string\",\"title\":\"string\",\"body\":\"string\",\"favorite\":false,"
        + "\"items\":[\"string\"],\"priority\":\"string\",\"due\":\"string\",\"name\":\"string\",\"date\":\"string\",\"time\":\"string\","
        + "\"repeat\":\"string\",\"recurrence\":\"string\",\"kind\":\"string\",\"location\":\"string\",\"meeting_url\":\"string\",\"alert_minutes\":0}]}";

    public AiAssistService(IGeminiClientFactory factory, AiActionExecutor executor)
    {
        _factory = factory;
        _executor = executor;
    }

    public bool IsConfigured => _factory.Create().IsConfigured;
    public string Provider => _factory.Provider;

    public Task<string> SummarizeAsync(string noteText, CancellationToken ct = default)
        => _factory.Create().CompleteAsync(SummarySystem, noteText, jsonSchema: null, ct);

    public Task<string> RephraseAsync(string text, CancellationToken ct = default)
        => _factory.Create().CompleteAsync(RephraseSystem, text, jsonSchema: null, ct);

    /// <summary>
    /// Plan a conversational turn. <paramref name="conversation"/> is the running transcript (most
    /// recent message last) so multi-turn refinements ("just once") keep context.
    /// </summary>
    public async Task<AiPlan> PlanAsync(string conversation, CancellationToken ct = default)
    {
        var system = AgentSystem + "\n\nCurrent date: " + DateTime.Now.ToString("yyyy-MM-dd (dddd)");
        var raw = await _factory.Create().CompleteAsync(system, conversation, AgentSchema, ct);
        return ParsePlan(raw);
    }

    public string Apply(IReadOnlyList<AiAction> actions, long? currentNoteId)
        => _executor.Apply(actions, currentNoteId);

    /// <summary>Parse the assistant turn: a {reply, actions} object, tolerating fences; falls back to a bare array.</summary>
    public static AiPlan ParsePlan(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new AiPlan();

        var objStart = raw.IndexOf('{');
        var objEnd = raw.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart)
        {
            try
            {
                var plan = JsonSerializer.Deserialize<AiPlan>(raw[objStart..(objEnd + 1)], JsonOpts);
                if (plan is not null && (!string.IsNullOrWhiteSpace(plan.Reply) || (plan.Actions?.Count ?? 0) > 0))
                {
                    plan.Actions = (plan.Actions ?? new()).Where(a => !string.IsNullOrWhiteSpace(a.Action)).ToList();
                    return plan;
                }
            }
            catch { /* fall through to bare-array parsing */ }
        }

        return new AiPlan { Actions = ParseActions(raw) };
    }

    /// <summary>Extract the JSON array from the model's reply (tolerates ```json fences / stray prose).</summary>
    public static List<AiAction> ParseActions(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start)
            return [];
        var json = raw[start..(end + 1)];
        try
        {
            var list = JsonSerializer.Deserialize<List<AiAction>>(json, JsonOpts);
            return list?.Where(a => !string.IsNullOrWhiteSpace(a.Action)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
