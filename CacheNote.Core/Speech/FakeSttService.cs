namespace CacheNote.Core.Speech;

/// <summary>
/// Offline fake provider (STT_PROVIDER=fake). Emits a canned interim→final transcript so the
/// whole dictation chain (mic toggle → partials → insert) can be verified without a network
/// call or microphone. Selected via the same provider switch as the real providers.
/// </summary>
public sealed class FakeSttService : ISpeechToTextService
{
    private CancellationTokenSource? _cts;

    public bool IsConfigured => true;
    public bool NeedsMicrophone => false;

    public event Action<string>? PartialReceived;
    public event Action<string>? FinalReceived;
    public event Action<string>? ErrorOccurred;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                string[] words = ["This", "is", "a", "live", "dictation", "test."];
                var acc = "";
                foreach (var w in words)
                {
                    if (token.IsCancellationRequested)
                        return;
                    acc = acc.Length == 0 ? w : acc + " " + w;
                    PartialReceived?.Invoke(acc);
                    await Task.Delay(220, token);
                }
                FinalReceived?.Invoke("This is a live dictation test. ");
            }
            catch (OperationCanceledException) { }
        }, token);
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> pcm16) => Task.CompletedTask;

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }
}
