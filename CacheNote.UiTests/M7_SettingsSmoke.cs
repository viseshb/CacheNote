using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// M7 gate smoke: the Settings page renders all sections, the theme selector works, the window-mode
/// buttons (compact / dock / restore) act without crashing, and back-to-home navigates. (Startup
/// toggle is NOT exercised here — it writes a real HKCU\Run entry.)
/// </summary>
public sealed class M7_SettingsSmoke
{
    [Fact]
    public void Settings_Render_Theme_WindowModes()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            w.FindFirstDescendant(c => c.ByAutomationId("settings"))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("SettingsTitle"))));
            Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("StartupToggle")));
            Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("PauseToggle")));
            TestApp.Screenshot(w, "m7-settings.png");

            // (Theme selector removed — the app is dark-mode only.)

            // Window-mode buttons act + restore.
            w.FindFirstDescendant(c => c.ByAutomationId("CompactButton"))!.AsButton().Invoke();
            Thread.Sleep(300);
            w.FindFirstDescendant(c => c.ByAutomationId("DockLeftButton"))!.AsButton().Invoke();
            Thread.Sleep(300);
            w.FindFirstDescendant(c => c.ByAutomationId("RestoreButton"))!.AsButton().Invoke();
            Thread.Sleep(300);

            // Back to home.
            w.FindFirstDescendant(c => c.ByAutomationId("BackToHome"))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("notes"))));
        }
        finally
        {
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
