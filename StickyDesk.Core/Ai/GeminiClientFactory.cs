using StickyDesk.Core.Cloud;

namespace StickyDesk.Core.Ai;

/// <summary>Creates the configured AI client: fake (offline), else the real Gemini/Vertex client.</summary>
public sealed class GeminiClientFactory : IGeminiClientFactory
{
    private readonly CloudConfig _cfg;

    public GeminiClientFactory(CloudConfig cfg) => _cfg = cfg;

    public string Provider => _cfg.AiProvider;

    public IGeminiClient Create() => _cfg.AiProvider == "fake" ? new FakeGeminiClient() : new GeminiClient(_cfg);
}
