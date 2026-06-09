using CacheNote.Core.Cloud;
using CacheNote.Core.Services;

namespace CacheNote.Core.Speech;

/// <summary>Creates a fresh <see cref="ISpeechToTextService"/> per dictation session, by provider.
/// The provider is chosen in Settings (persisted as "stt_provider"); falls back to the .env default.</summary>
public sealed class SpeechToTextFactory : ISpeechToTextFactory
{
    private readonly CloudConfig _cfg;
    private readonly ISettingsService _settings;

    public SpeechToTextFactory(CloudConfig cfg, ISettingsService settings)
    {
        _cfg = cfg;
        _settings = settings;
    }

    private string Resolve()
    {
        var chosen = _settings.Get("stt_provider");
        return string.IsNullOrWhiteSpace(chosen) ? _cfg.SttProvider : chosen.Trim().ToLowerInvariant();
    }

    public string Provider => Resolve();

    public ISpeechToTextService Create() => Resolve() switch
    {
        "fake" => new FakeSttService(),
        "assemblyai" => new AssemblyAiSttService(_cfg),
        _ => new DeepgramSttService(_cfg),
    };
}
