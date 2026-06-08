using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// M5 gate smoke (fake provider): with STT_PROVIDER=fake the whole dictation chain runs without a
/// network call or microphone — toggling the mic streams a canned transcript that is committed
/// into the editor. The real Deepgram/AssemblyAI WebSocket path needs live keys (manual check).
/// </summary>
public sealed class M5_DictationSmoke
{
    [Fact]
    public void Fake_Dictation_Inserts_Into_Editor()
    {
        Environment.SetEnvironmentVariable("STT_PROVIDER", "fake");
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            var body = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorBody")));
            var mic = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("MicButton"))?.AsToggleButton());

            mic.Toggle();   // start dictation (fake emits over ~1.4s, then a final)
            Thread.Sleep(3000);

            var text = body.Patterns.Text.Pattern.DocumentRange.GetText(-1);
            TestApp.Screenshot(w, "m5-dictation.png");
            Assert.Contains("dictation", text, StringComparison.OrdinalIgnoreCase);

            mic.Toggle();   // stop
        }
        finally
        {
            Environment.SetEnvironmentVariable("STT_PROVIDER", null);
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static T WaitFor<T>(Func<T?> get, int timeoutSeconds = 8) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var v = get();
            if (v is not null) return v;
            Thread.Sleep(250);
        }
        throw new TimeoutException("Element did not appear in time.");
    }
}
