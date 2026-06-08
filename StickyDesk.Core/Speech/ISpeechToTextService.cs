namespace StickyDesk.Core.Speech;

/// <summary>
/// A live speech-to-text session. The app feeds 16 kHz mono PCM16 frames via
/// <see cref="SendAsync"/>; the provider streams back interim (<see cref="PartialReceived"/>)
/// and finalized (<see cref="FinalReceived"/>) transcripts, already formatted by the provider.
/// One instance == one dictation session (open on Start, closed on Stop).
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>False when the required API key is missing (the UI shows a hint instead of dictating).</summary>
    bool IsConfigured { get; }

    /// <summary>True if real microphone audio must be fed in (false for the fake provider).</summary>
    bool NeedsMicrophone { get; }

    event Action<string>? PartialReceived;
    event Action<string>? FinalReceived;
    event Action<string>? ErrorOccurred;

    Task StartAsync(CancellationToken ct);
    Task SendAsync(ReadOnlyMemory<byte> pcm16);
    Task StopAsync();
}

/// <summary>Creates the configured <see cref="ISpeechToTextService"/> per dictation session.</summary>
public interface ISpeechToTextFactory
{
    string Provider { get; }
    ISpeechToTextService Create();
}
