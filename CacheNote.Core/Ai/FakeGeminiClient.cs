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
            // Canned conversational plan referencing the user's instruction; exercises every action type.
            var safe = userPrompt.Replace("\\", "/").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
            if (safe.Length > 60) safe = safe[..60];
            var json = """
            {
              "reply": "Sure — here's a plan for '{INSTR}'. I'll add a note, a task, a reminder, and a calendar event. Apply when ready.",
              "actions": [
                { "action": "create_note", "title": "Plan: {INSTR}", "body": "Drafted by AI assistant.", "favorite": true },
                { "action": "add_checklist", "items": ["Research", "Outline", "Draft", "Review"] },
                { "action": "create_task", "title": "Follow up on {INSTR}", "priority": "high" },
                { "action": "create_reminder", "title": "{INSTR}", "date": "2026-12-31", "time": "09:00", "repeat": "once" },
                { "action": "create_event", "title": "{INSTR} kickoff", "date": "2026-12-31", "kind": "meeting", "alert_minutes": 10 },
                { "action": "add_tag", "name": "ai" }
              ]
            }
            """.Replace("{INSTR}", safe);
            return Task.FromResult(json);
        }

        return Task.FromResult($"[AI summary] {Truncate(userPrompt, 160)}");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
