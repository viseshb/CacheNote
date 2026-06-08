using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// V2 calendar: create a real event (title + meeting link) via the Add-event dialog, then confirm
/// it shows in the agenda and the Agenda view renders it. Screenshots for the review gate.
/// </summary>
public sealed class V2_CalendarSmoke
{
    private static AutomationElement? WaitFor(Func<AutomationElement?> find, int timeoutSeconds = 8)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var el = find();
            if (el is not null)
                return el;
            Thread.Sleep(200);
        }
        return null;
    }

    [Fact]
    public void AddEvent_ShowsInAgenda()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("calendar")))!.AsButton().Invoke();
            // ItemsRepeater AutomationIds don't surface to UIA — assert a real control instead.
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("CalPeriod"))));
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddEventButton"))));
            TestApp.Screenshot(w, "v2-cal-01-month.png");

            // Open the new-event dialog and fill it in.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddEventButton")))!.AsButton().Invoke();
            var titleBox = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EventTitle")));
            Assert.NotNull(titleBox);
            titleBox!.AsTextBox().Enter("Team Sync");

            var urlBox = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EventMeetingUrl")));
            urlBox?.AsTextBox().Enter("https://meet.google.com/abc-defg-hij");
            TestApp.Screenshot(w, "v2-cal-02-dialog.png");

            // Save (ContentDialog primary button).
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Save")))!.AsButton().Invoke();
            Thread.Sleep(700);

            // The event (today) should appear in the selected-day agenda.
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Team Sync"))));
            TestApp.Screenshot(w, "v2-cal-03-event-added.png");

            // Switch to Agenda view → it lists upcoming events.
            var mode = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("CalendarViewMode")));
            mode!.AsComboBox().Select("Agenda");
            Thread.Sleep(500);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Team Sync"))));
            TestApp.Screenshot(w, "v2-cal-04-agenda.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }
}
