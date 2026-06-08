using System.Text.Json;

namespace StickyDesk.Core.Ai;

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
        "You are a productivity assistant. Turn the user's request into a JSON array of actions. "
        + "Allowed actions: "
        + "{\"action\":\"create_note\",\"title\":\"...\",\"body\":\"...\"}, "
        + "{\"action\":\"add_checklist\",\"items\":[\"...\"]}, "
        + "{\"action\":\"create_task\",\"title\":\"...\",\"priority\":\"low|medium|high\",\"due\":\"YYYY-MM-DD\"}, "
        + "{\"action\":\"add_tag\",\"name\":\"...\"}. "
        + "Respond with ONLY the JSON array, no prose.";
    private const string AgentSchema = "[{\"action\":\"string\",\"title\":\"string\",\"body\":\"string\",\"items\":[\"string\"],\"priority\":\"string\",\"due\":\"string\",\"name\":\"string\"}]";

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

    public async Task<List<AiAction>> PlanAsync(string instruction, CancellationToken ct = default)
    {
        var raw = await _factory.Create().CompleteAsync(AgentSystem, instruction, AgentSchema, ct);
        return ParseActions(raw);
    }

    public string Apply(IReadOnlyList<AiAction> actions, long? currentNoteId)
        => _executor.Apply(actions, currentNoteId);

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
