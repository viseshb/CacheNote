using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CacheNote.Core.Cloud;

namespace CacheNote.Core.Speech;

/// <summary>
/// AssemblyAI live streaming (v3 Universal-Streaming) over a raw WebSocket — the official .NET SDK
/// is discontinued. Consumes AssemblyAI's own Turn detection + formatting (no custom NLP).
/// Best-effort: the live path needs a real ASSEMBLYAI_API_KEY.
/// </summary>
public sealed class AssemblyAiSttService : ISpeechToTextService
{
    private readonly CloudConfig _cfg;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public AssemblyAiSttService(CloudConfig cfg) => _cfg = cfg;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cfg.AssemblyAiKey);
    public bool NeedsMicrophone => true;

    public event Action<string>? PartialReceived;
    public event Action<string>? FinalReceived;
    public event Action<string>? ErrorOccurred;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            ErrorOccurred?.Invoke("Add ASSEMBLYAI_API_KEY to .env to dictate.");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", _cfg.AssemblyAiKey);

        var url = "wss://streaming.assemblyai.com/v3/ws?sample_rate=16000&encoding=pcm_s16le&format_turns=true";
        try
        {
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("AssemblyAI connect failed: " + ex.Message);
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> pcm16)
    {
        if (_ws is { State: WebSocketState.Open } && _cts is not null)
        {
            try { await _ws.SendAsync(pcm16, WebSocketMessageType.Binary, true, _cts.Token); }
            catch { /* dropped frame */ }
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_ws is { State: WebSocketState.Open })
            {
                var terminate = Encoding.UTF8.GetBytes("{\"type\":\"Terminate\"}");
                await _ws.SendAsync(terminate, WebSocketMessageType.Text, true, CancellationToken.None);
                // CloseOutputAsync, NOT CloseAsync: CloseAsync performs its own receive, which
                // collides with the receive loop's pending ReceiveAsync (only one allowed) and
                // threw InvalidOperationException — no clean handshake ever happened. Then give
                // the loop a moment to drain the final Turn AssemblyAI flushes after Terminate,
                // so the last utterance before toggling the mic off isn't dropped.
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                await Task.Delay(750);
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
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "Turn")
                return;

            var transcript = root.TryGetProperty("transcript", out var t) ? t.GetString() ?? "" : "";
            if (transcript.Length == 0)
                return;

            var endOfTurn = root.TryGetProperty("end_of_turn", out var e) && e.GetBoolean();
            if (endOfTurn)
                FinalReceived?.Invoke(transcript);
            else
                PartialReceived?.Invoke(transcript);
        }
        catch { /* control frame */ }
    }
}
