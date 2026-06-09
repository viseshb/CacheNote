using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// Title-bar quick actions added in the rebrand: the left-corner pin (always-on-top) toggle must
/// actually flip its checked state, and the gear must navigate to Settings.
/// </summary>
public sealed class E2E_TitleBar
{
    [Fact]
    public void PinToggle_Flips_And_Gear_Opens_Settings()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            // Pin toggle: Off → On → Off (it's a real ToggleButton, so its checked state tracks).
            var pin = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("PinToggle"))?.AsToggleButton());
            pin.Toggle();
            Thread.Sleep(300);
            Assert.Equal(ToggleState.On, pin.ToggleState);
            TestApp.Screenshot(w, "e2e-titlebar-pinned.png");
            pin.Toggle();
            Thread.Sleep(300);
            Assert.Equal(ToggleState.Off, pin.ToggleState);

            // Gear → Settings.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("SettingsGear"))?.AsButton()).Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("ThemeSetting"))));
            TestApp.Screenshot(w, "e2e-titlebar-gear-settings.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static T WaitFor<T>(Func<T?> get, int timeoutSeconds = 10) where T : class
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
