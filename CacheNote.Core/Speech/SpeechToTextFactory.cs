using CacheNote.Core.Cloud;

namespace CacheNote.Core.Speech;

/// <summary>Creates a fresh <see cref="ISpeechToTextService"/> per dictation session, by provider.</summary>
public sealed class SpeechToTextFactory : ISpeechToTextFactory
{
    private readonly CloudConfig _cfg;

    public SpeechToTextFactory(CloudConfig cfg) => _cfg = cfg;

    public string Provider => _cfg.SttProvider;

    public ISpeechToTextService Create() => _cfg.SttProvider switch
    {
        "fake" => new FakeSttService(),
        "assemblyai" => new AssemblyAiSttService(_cfg),
        _ => new DeepgramSttService(_cfg),
    };
}
