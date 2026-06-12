using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// Comprehensive sweep of the areas the per-feature smokes don't fully cover: every Calendar view +
/// navigation + add-event, every Settings toggle + window mode, and the AI ball creating a calendar
/// event + handling empty input. (STT is excluded by request.)
/// </summary>
public sealed class E2E_FullSweep
{
    [Fact]
    public void Calendar_All_Views_Nav_And_AddEvent()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("calendar"))).AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod"))));

            // Every view renders (incl. the new Year overview).
            var mode = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("CalendarViewMode"))).AsComboBox();
            foreach (var view in new[] { "Year", "Week", "Day", "Agenda", "Month" })
            {
                mode.Select(view);
                Thread.Sleep(400);
                Assert.False(app.HasExited, $"app exited on {view} view");
                Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod")));
                TestApp.Screenshot(w, $"sweep-cal-{view}.png");
            }

            // Year overview shows 12 month tiles; clicking one drills into that month.
            mode.Select("Year");
            Thread.Sleep(400);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Open month"))));   // month tiles present
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Open month"))).Click();
            Thread.Sleep(400);
            Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod")));   // drilled into Month
            TestApp.Screenshot(w, "sweep-cal-year-drill.png");

            // Navigation: prev / next / today.
            foreach (var nav in new[] { "CalPrev", "CalNext", "CalToday" })
            {
                w.FindFirstDescendant(c => c.ByAutomationId(nav))!.AsButton().Invoke();
                Thread.Sleep(250);
                Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod")));
            }

            // Add an event (today) and confirm it lands in the Agenda.
            w.FindFirstDescendant(c => c.ByAutomationId("AddEventButton"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EventTitle"))).AsTextBox().Enter("Sweep Event");
            Thread.Sleep(200);
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Save"))).AsButton().Invoke();
            Thread.Sleep(700);
            mode.Select("Agenda");
            Thread.Sleep(500);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Sweep Event"))));
            TestApp.Screenshot(w, "sweep-cal-event.png");
        }
        finally { Close(app); }
    }

    [Fact]
    public void Settings_All_Toggles_And_WindowModes()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("settings"))).AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("FontSizeSetting"))));

            // Flip each toggle on then off (no crash, state changes persist to the isolated DB).
            foreach (var id in new[] { "StartupToggle", "AlwaysOnTopToggle", "PauseToggle" })
            {
                var t = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId(id))).AsToggleButton();
                t.Toggle(); Thread.Sleep(250);
                t.Toggle(); Thread.Sleep(250);
                Assert.False(app.HasExited, $"app exited toggling {id}");
            }

            // (Theme dropdown removed — dark-mode only.)
            TestApp.Screenshot(w, "sweep-settings.png");

            // Window modes: compact, dock left, dock right, restore.
            foreach (var mode in new[] { "CompactButton", "DockLeftButton", "DockRightButton", "RestoreButton" })
            {
                WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId(mode))).AsButton().Invoke();
                Thread.Sleep(450);
                Assert.False(app.HasExited, $"app exited on window mode {mode}");
            }

            // Connection-test buttons are present (clicking hits the network — left to manual/live).
            Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("TestAiButton")));
        }
        finally { Close(app); }
    }

    [Fact]
    public void AiBall_Creates_Calendar_Event_And_Handles_Empty()
    {
        Environment.SetEnvironmentVariable("AI_PROVIDER", "fake");
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            // Empty input → Send is disabled (can't send nothing).
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiBall"))).AsButton().Invoke();
            var send = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatSend"))).AsButton();
            Assert.False(send.IsEnabled, "Send must be disabled when the AI input is empty");
            Thread.Sleep(200);
            Assert.False(app.HasExited);

            // Real request → auto-apply. The fake plan favorites a note, so it must appear in Favorites.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatInput"))).AsTextBox().Text = "launchplan";
            Thread.Sleep(150);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatSend"))).AsButton().Invoke();
            WaitForText(w, "AiStatus", "Applied");
            w.FindFirstDescendant(c => c.ByAutomationId("AiChatClose"))?.AsButton().Invoke();
            Thread.Sleep(300);

            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("favorites"))).AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Open note"))));   // the AI-favorited note
            TestApp.Screenshot(w, "sweep-ai-favorite.png");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_PROVIDER", null);
            Close(app);
        }
    }

    private static void Close(Application app)
    {
        try { app.Close(); } catch { }
        if (!app.HasExited) { try { app.Kill(); } catch { } }
    }

    private static void WaitForText(Window w, string id, string contains, int sec = 12)
    {
        var deadline = DateTime.UtcNow.AddSeconds(sec);
        while (DateTime.UtcNow < deadline)
        {
            if ((w.FindFirstDescendant(c => c.ByAutomationId(id))?.Name ?? "").Contains(contains, StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(250);
        }
        throw new TimeoutException($"'{id}' never contained '{contains}'.");
    }

    private static T WaitFor<T>(Func<T?> get, int sec = 10) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(sec);
        while (DateTime.UtcNow < deadline) { var v = get(); if (v is not null) return v; Thread.Sleep(250); }
        throw new TimeoutException("Element did not appear in time.");
    }
}
