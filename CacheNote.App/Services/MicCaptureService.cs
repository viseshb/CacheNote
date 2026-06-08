using NAudio.Wave;

namespace CacheNote_App.Services;

/// <summary>
/// Captures microphone audio as 16 kHz mono PCM16 (the format the STT providers expect) and
/// raises <see cref="FrameReady"/> for each buffer. Constructed only when the user starts
/// dictation — never at app startup — so a missing/blocked mic can't break launch.
/// </summary>
public sealed class MicCaptureService : IDisposable
{
    private WaveInEvent? _wave;

    /// <summary>Raised on the capture thread with a fresh 16 kHz mono PCM16 buffer.</summary>
    public event Action<byte[]>? FrameReady;

    /// <summary>Start capture. Returns false (and reports via <paramref name="onError"/>) on failure.</summary>
    public bool Start(Action<string> onError)
    {
        try
        {
            _wave = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
            };
            _wave.DataAvailable += OnDataAvailable;
            _wave.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                    onError(e.Exception.Message);
            };
            _wave.StartRecording();
            return true;
        }
        catch (Exception ex)
        {
            onError("Microphone unavailable. In Windows Settings → Privacy → Microphone, allow desktop apps. (" + ex.Message + ")");
            Dispose();
            return false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
            return;
        var frame = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, frame, e.BytesRecorded);
        FrameReady?.Invoke(frame);
    }

    public void Dispose()
    {
        try
        {
            if (_wave is not null)
            {
                _wave.DataAvailable -= OnDataAvailable;
                _wave.StopRecording();
                _wave.Dispose();
            }
        }
        catch { /* ignore */ }
        _wave = null;
    }
}
