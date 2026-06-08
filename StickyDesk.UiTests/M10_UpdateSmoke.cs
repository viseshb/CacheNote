using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace StickyDesk.UiTests;

/// <summary>
/// M10 gate smoke: the title-bar Update button runs a check and reports a result (here, with no
/// GITHUB_OWNER/REPO configured, it reports the installed version + a "not configured" message).
/// The real release→download→install loop needs a GitHub repo (manual check).
/// </summary>
public sealed class M10_UpdateSmoke
{
    [Fact]
    public void UpdateButton_ChecksAndReports()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            w.FindFirstDescendant(c => c.ByAutomationId("UpdateButton"))!.AsButton().Invoke();

            // The dialog reports the installed version once the check returns.
            WaitForText(w, "UpdateStatus", "Installed version");
            TestApp.Screenshot(w, "m10-update.png");

            w.FindFirstDescendant(c => c.ByName("Close"))?.AsButton().Invoke();
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static void WaitForText(Window w, string automationId, string contains, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var el = w.FindFirstDescendant(c => c.ByAutomationId(automationId));
            if ((el?.Name ?? "").Contains(contains, StringComparison.OrdinalIgnoreCase))
                return;
            Thread.Sleep(250);
        }
        throw new TimeoutException($"'{automationId}' never contained '{contains}'.");
    }
}
