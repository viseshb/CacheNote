using CacheNote.Core.Cloud;

namespace CacheNote.Core.Ai;

/// <summary>Creates the configured AI client: fake (offline), else the real Gemini/Vertex client.</summary>
public sealed class GeminiClientFactory : IGeminiClientFactory
{
    private readonly CloudConfig _cfg;

    // Shared across every Create() so cache survives the per-call client churn (clients are newed
    // up each use so a provider/key change is picked up without a restart). Keyed by provider|model
    // so a model switch can never serve a stale answer.
    private readonly LlmResponseCache _cache;
    private readonly Func<CloudConfig, IGeminiClient> _newInner;

    public GeminiClientFactory(CloudConfig cfg)
        : this(cfg, c => c.AiProvider == "fake" ? new FakeGeminiClient() : new GeminiClient(c), new LlmResponseCache())
    {
    }

    // Test seam: inject the inner client builder + cache so caching behavior can be observed
    // through the exact production wrapping/sharing.
    internal GeminiClientFactory(CloudConfig cfg, Func<CloudConfig, IGeminiClient> newInner, LlmResponseCache cache)
    {
        _cfg = cfg;
        _newInner = newInner;
        _cache = cache;
    }

    public string Provider => _cfg.AiProvider;

    public IGeminiClient Create()
        => new CachingGeminiClient(_newInner(_cfg), _cache, _cfg.AiProvider + "|" + _cfg.GeminiModel);
}
