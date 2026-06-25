using CacheNote.Core.Cloud;

namespace CacheNote.Core.Ai;

/// <summary>Creates the configured AI client: fake (offline), else the real Gemini/Vertex client.</summary>
public sealed class GeminiClientFactory : IGeminiClientFactory
{
    private readonly CloudConfig _cfg;

    // Shared across every Create() so cache survives the per-call client churn (clients are newed
    // up each use so a provider/key change is picked up without a restart). Keyed by provider|model
    // so a model switch can never serve a stale answer.
    private readonly LlmResponseCache _cache = new();

    public GeminiClientFactory(CloudConfig cfg) => _cfg = cfg;

    public string Provider => _cfg.AiProvider;

    public IGeminiClient Create()
    {
        IGeminiClient inner = _cfg.AiProvider == "fake" ? new FakeGeminiClient() : new GeminiClient(_cfg);
        return new CachingGeminiClient(inner, _cache, _cfg.AiProvider + "|" + _cfg.GeminiModel);
    }
}
