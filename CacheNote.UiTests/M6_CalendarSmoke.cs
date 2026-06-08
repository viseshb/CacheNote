using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// M6 gate smoke: the Calendar section renders a day grid, navigates months, switches to the
/// Week view, and "Today" returns to the current period.
/// </summary>
public sealed class M6_CalendarSmoke
{
    [Fact]
    public void Calendar_Renders_Navigates_SwitchesView()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            w.FindFirstDescendant(c => c.ByAutomationId("calendar"))!.AsButton().Invoke();

            // Day cells render + a period label is shown.
            var period = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod")));
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Day"))));
            var monthLabel = period.AsLabel().Text;
            Assert.False(string.IsNullOrWhiteSpace(monthLabel));
            TestApp.Screenshot(w, "m6-calendar.png");

            // Navigate to next month → label changes.
            w.FindFirstDescendant(c => c.ByAutomationId("CalNext"))!.AsButton().Invoke();
            Thread.Sleep(300);
            var nextLabel = w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod"))!.AsLabel().Text;
            Assert.NotEqual(monthLabel, nextLabel);

            // Back to today.
            w.FindFirstDescendant(c => c.ByAutomationId("CalToday"))!.AsButton().Invoke();
            Thread.Sleep(300);
            Assert.Equal(monthLabel, w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod"))!.AsLabel().Text);

            // Switch to Week view → period label becomes a "Week of …".
            var mode = w.FindFirstDescendant(c => c.ByAutomationId("CalendarViewMode"))!.AsComboBox();
            mode.Select(1);
            Thread.Sleep(400);
            var weekLabel = w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod"))!.AsLabel().Text;
            Assert.Contains("Week", weekLabel);
            TestApp.Screenshot(w, "m6-calendar-week.png");
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
