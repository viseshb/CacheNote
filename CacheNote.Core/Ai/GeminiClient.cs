using System.Net.Http;
using System.Text;
using System.Text.Json;
using CacheNote.Core.Cloud;

namespace CacheNote.Core.Ai;

/// <summary>
/// Gemini via Vertex AI Express (header x-goog-api-key) with a Google AI Studio fallback
/// (?key=). Raw HttpClient, no SDK. thinkingBudget=0 for low latency. Best-effort: needs a real
/// VERTEX_AI_API_KEY or GEMINI_API_KEY and can only be verified with one.
/// </summary>
public sealed class GeminiClient : IGeminiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly CloudConfig _cfg;

    public GeminiClient(CloudConfig cfg) => _cfg = cfg;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cfg.VertexKey) || !string.IsNullOrWhiteSpace(_cfg.GeminiKey);

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("No AI key configured. Add VERTEX_AI_API_KEY or GEMINI_API_KEY to .env.");

        var useVertex = !string.IsNullOrWhiteSpace(_cfg.VertexKey);
        var model = _cfg.GeminiModel;

        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = 0.2,
            ["maxOutputTokens"] = 2048,
            ["thinkingConfig"] = new { thinkingBudget = 0 },
        };
        if (jsonSchema is not null)
            generationConfig["responseMimeType"] = "application/json";

        var system = jsonSchema is null ? systemPrompt : systemPrompt + "\n\nReturn ONLY JSON matching:\n" + jsonSchema;

        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            systemInstruction = new { parts = new[] { new { text = system } } },
            generationConfig,
        };

        // Both providers accept the x-goog-api-key header; never put the key in the URL
        // (query strings end up in proxies/traces).
        var url = useVertex
            ? $"{_cfg.VertexBaseUrl}/publishers/google/models/{model}:generateContent"
            : $"{_cfg.GeminiBaseUrl}/models/{model}:generateContent";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("x-goog-api-key", useVertex ? _cfg.VertexKey : _cfg.GeminiKey);

        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI request failed ({(int)resp.StatusCode}). {Truncate(text, 300)}");

        // A safety-blocked prompt returns HTTP 200 with NO candidates (promptFeedback.blockReason
        // only), and a blocked/empty candidate can lack content/parts — GetProperty would throw
        // KeyNotFoundException and the raw exception text landed in the chat bubble.
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            var reason = doc.RootElement.TryGetProperty("promptFeedback", out var fb) &&
                         fb.TryGetProperty("blockReason", out var br) ? br.GetString() : null;
            throw new InvalidOperationException(reason is null
                ? "The AI returned an empty response. Try rephrasing."
                : $"The AI blocked this request ({reason}). Try rephrasing.");
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            var finish = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
            throw new InvalidOperationException(finish == "MAX_TOKENS"
                ? "The AI response was cut off — try a shorter request."
                : $"The AI returned no text ({finish ?? "empty"}). Try rephrasing.");
        }

        var sb = new StringBuilder();
        foreach (var p in parts.EnumerateArray())
            if (p.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        return sb.ToString();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
