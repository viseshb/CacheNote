using System.Net.Http;
using System.Text;
using System.Text.Json;
using StickyDesk.Core.Cloud;

namespace StickyDesk.Core.Ai;

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

        var url = useVertex
            ? $"{_cfg.VertexBaseUrl}/publishers/google/models/{model}:generateContent"
            : $"{_cfg.GeminiBaseUrl}/models/{model}:generateContent?key={_cfg.GeminiKey}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (useVertex)
            req.Headers.TryAddWithoutValidation("x-goog-api-key", _cfg.VertexKey);

        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI request failed ({(int)resp.StatusCode}). {Truncate(text, 300)}");

        using var doc = JsonDocument.Parse(text);
        var parts = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
        var sb = new StringBuilder();
        foreach (var p in parts.EnumerateArray())
            if (p.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        return sb.ToString();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
