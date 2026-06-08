using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace StickyDesk.UiTests;

/// <summary>
/// M3 gate smoke: from the hub, open Reminders, create one, see it listed, complete it,
/// then delete it. (Toast firing + buttons are shell UI — verified live, not here.)
/// Reminders persist in the app DB, so the test clears existing rows first.
/// </summary>
public sealed class M3_RemindersSmoke
{
    [Fact]
    public void Create_Complete_Delete_Reminder()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            // Hub → Reminders.
            w.FindFirstDescendant(c => c.ByAutomationId("reminders"))!.AsButton().Invoke();
            var addBtn = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddReminder"))?.AsButton());

            // Start from a clean slate (reminders persist across runs).
            ClearAll(w);

            // Type a message; date/time default to ~5 min out, repeat = Once.
            var message = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("ReminderMessage"))?.AsTextBox());
            message.Text = "Test reminder";
            Thread.Sleep(200);

            addBtn!.Invoke();
            Thread.Sleep(600);

            // A row now exists (its action buttons appear).
            var delete = WaitFor(() => w.FindFirstDescendant(c => c.ByName("Delete reminder"))?.AsButton());
            Assert.NotNull(delete);
            TestApp.Screenshot(w, "m3-reminders.png");

            // Complete it.
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Complete reminder"))?.AsButton())!.Invoke();
            Thread.Sleep(500);

            // Delete it → no rows left.
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Delete reminder"))?.AsButton())!.Invoke();
            Thread.Sleep(600);
            Assert.Null(w.FindFirstDescendant(c => c.ByName("Delete reminder")));
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static void ClearAll(Window w)
    {
        for (var i = 0; i < 25; i++)
        {
            var del = w.FindFirstDescendant(c => c.ByName("Delete reminder"))?.AsButton();
            if (del is null)
                return;
            del.Invoke();
            Thread.Sleep(350);
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
