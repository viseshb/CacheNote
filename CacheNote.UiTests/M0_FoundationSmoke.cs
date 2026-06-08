using FlaUI.Core;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// M0 gate smoke: the built app launches, shows its main window titled
/// "CacheNote", and a screenshot is captured for the review gate.
/// </summary>
public sealed class M0_FoundationSmoke
{
    [Fact]
    public void App_Launches_And_Shows_Main_Window()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var window = TestApp.WaitForMainWindow(app, automation);

            Assert.Contains("CacheNote", window.Title);

            TestApp.Screenshot(window, "m0-foundation.png");
        }
        finally
        {
            try { app.Close(); } catch { /* ignore */ }
            if (!app.HasExited)
            {
                try { app.Kill(); } catch { /* ignore */ }
            }
        }
    }
}
