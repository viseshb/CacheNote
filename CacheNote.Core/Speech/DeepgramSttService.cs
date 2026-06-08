using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CacheNote.Core.Cloud;

namespace CacheNote.Core.Speech;

/// <summary>
/// Deepgram live streaming over a raw WebSocket (no SDK). Streams 16 kHz mono PCM16 and consumes
/// Deepgram's own interim/final + smart-format/punctuation — no custom NLP. Best-effort: the live
/// path needs a real DEEPGRAM_API_KEY and can only be verified with one.
/// </summary>
public sealed class DeepgramSttService : ISpeechToTextService
{
    private readonly CloudConfig _cfg;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public DeepgramSttService(CloudConfig cfg) => _cfg = cfg;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cfg.DeepgramKey);
    public bool NeedsMicrophone => true;

    public event Action<string>? PartialReceived;
    public event Action<string>? FinalReceived;
    public event Action<string>? ErrorOccurred;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            ErrorOccurred?.Invoke("Add DEEPGRAM_API_KEY to .env to dictate.");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {_cfg.DeepgramKey}");

        var model = Uri.EscapeDataString(_cfg.DeepgramModel);
        var url = $"wss://api.deepgram.com/v1/listen?model={model}&encoding=linear16&sample_rate=16000&channels=1"
                + "&interim_results=true&smart_format=true&punctuate=true&utterance_end_ms=1000";

        try
        {
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("Deepgram connect failed: " + ex.Message);
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> pcm16)
    {
        if (_ws is { State: WebSocketState.Open } && _cts is not null)
        {
            try { await _ws.SendAsync(pcm16, WebSocketMessageType.Binary, true, _cts.Token); }
            catch { /* dropped frame; ignore */ }
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_ws is { State: WebSocketState.Open })
            {
                var close = Encoding.UTF8.GetBytes("{\"type\":\"CloseStream\"}");
                await _ws.SendAsync(close, WebSocketMessageType.Text, true, CancellationToken.None);
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch { /* ignore */ }
        _cts?.Cancel();
        _ws?.Dispose();
        _ws = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult res;
                do
                {
                    res = await _ws.ReceiveAsync(buffer, ct);
                    if (res.MessageType == WebSocketMessageType.Close)
                        return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                }
                while (!res.EndOfMessage);

                ParseMessage(sb.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorOccurred?.Invoke(ex.Message); }
    }

    private void ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("channel", out var channel))
                return;
            if (!channel.TryGetProperty("alternatives", out var alts) || alts.GetArrayLength() == 0)
                return;

            var transcript = alts[0].TryGetProperty("transcript", out var t) ? t.GetString() ?? "" : "";
            if (transcript.Length == 0)
                return;

            var isFinal = root.TryGetProperty("is_final", out var f) && f.GetBoolean();
            if (isFinal)
                FinalReceived?.Invoke(transcript);
            else
                PartialReceived?.Invoke(transcript);
        }
        catch { /* non-transcript control frame */ }
    }
}
