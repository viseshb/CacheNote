namespace CacheNote.Core.Ai;

/// <summary>
/// Offline fake AI (AI_PROVIDER=fake). Returns canned output so the whole AI chain
/// (command → preview → apply) can be verified without a network call or key. When a JSON
/// schema is requested (agentic), returns a small canned action list.
/// </summary>
public sealed class FakeGeminiClient : IGeminiClient
{
    public bool IsConfigured => true;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default)
    {
        if (jsonSchema is not null)
        {
            // Canned agentic plan referencing the user's instruction.
            var safe = userPrompt.Replace("\"", "'");
            var json = """
            [
              { "action": "create_note", "title": "Plan: {INSTR}", "body": "Drafted by AI assistant." },
              { "action": "add_checklist", "items": ["Research", "Outline", "Draft", "Review"] },
              { "action": "create_task", "title": "Follow up on {INSTR}", "priority": "high" },
              { "action": "add_tag", "name": "ai" }
            ]
            """.Replace("{INSTR}", safe);
            return Task.FromResult(json);
        }

        return Task.FromResult($"[AI summary] {Truncate(userPrompt, 160)}");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
