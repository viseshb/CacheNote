using System.Net;
using System.Net.Http;
using StickyDesk.Core.Ai;

namespace StickyDesk.Core.Cloud;

/// <summary>
/// Live, on-demand connectivity checks for the cloud providers (M5 STT / M8 AI).
/// Each returns (ok, human message) and never throws to the caller. Distinguishes an auth
/// failure (bad key) from other HTTP errors (wrong model/endpoint) so the UI can report which.
/// Keys are taken from <see cref="CloudConfig"/> and never logged.
/// </summary>
public sealed class CloudConnectivity
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CloudConfig _cfg;
    private readonly IGeminiClientFactory _aiFactory;

    public CloudConnectivity(CloudConfig cfg, IGeminiClientFactory aiFactory)
    {
        _cfg = cfg;
        _aiFactory = aiFactory;
    }

    public async Task<(bool Ok, string Message)> TestDeepgramAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.DeepgramKey))
            return (false, "Deepgram: no key in .env");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/projects");
            req.Headers.TryAddWithoutValidation("Authorization", $"Token {_cfg.DeepgramKey}");
            using var r = await Http.SendAsync(req, ct);
            if (r.IsSuccessStatusCode)
                return (true, "Deepgram: connected ✓");
            return (false, r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Deepgram: key rejected (auth)"
                : $"Deepgram: HTTP {(int)r.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"Deepgram: {ex.Message}");
        }
    }

    public async Task<(bool Ok, string Message)> TestAssemblyAiAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.AssemblyAiKey))
            return (false, "AssemblyAI: no key in .env");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.assemblyai.com/v2/transcript?limit=1");
            req.Headers.TryAddWithoutValidation("Authorization", _cfg.AssemblyAiKey);
            using var r = await Http.SendAsync(req, ct);
            if (r.IsSuccessStatusCode)
                return (true, "AssemblyAI: connected ✓");
            return (false, r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "AssemblyAI: key rejected (auth)"
                : $"AssemblyAI: HTTP {(int)r.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"AssemblyAI: {ex.Message}");
        }
    }

    /// <summary>Real one-token round-trip through the configured AI provider (Vertex/Gemini).</summary>
    public async Task<(bool Ok, string Message)> TestAiAsync(CancellationToken ct = default)
    {
        var client = _aiFactory.Create();
        if (!client.IsConfigured)
            return (false, "AI: no key in .env (VERTEX_AI_API_KEY or GEMINI_API_KEY)");
        try
        {
            await client.CompleteAsync("You are a connectivity check. Reply with the single word OK.", "ping", null, ct);
            return (true, $"AI: connected ✓ ({_cfg.AiProvider} · {_cfg.GeminiModel})");
        }
        catch (Exception ex)
        {
            // GeminiClient surfaces the HTTP status in its message (e.g. 404 = wrong model/endpoint).
            return (false, $"AI: {ex.Message}");
        }
    }
}
