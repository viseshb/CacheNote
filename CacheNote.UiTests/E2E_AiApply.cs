using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// Reproduces the user's report: after the AI ball plans + Apply, the created content must actually
/// appear in the Tasks and Reminders sections (not just an "Applied" status). Uses the fake provider.
/// </summary>
public sealed class E2E_AiApply
{
    [Fact]
    public void Ball_Apply_Creates_Visible_Task_And_Reminder()
    {
        Environment.SetEnvironmentVariable("AI_PROVIDER", "fake");
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            // Open ball → type → Send → Apply.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiBall"))).AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatInput"))?.AsTextBox()).Text = "set up my launch";
            Thread.Sleep(150);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatSend"))).AsButton().Invoke();
            // Acts by default (auto-applies on send).
            WaitForText(w, "AiStatus", "Applied");
            TestApp.Screenshot(w, "e2e-aiapply-applied.png");
            w.FindFirstDescendant(c => c.ByAutomationId("AiChatClose"))?.AsButton().Invoke();
            Thread.Sleep(300);

            // The fake plan creates a task → it must show on the Tasks page.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("tasks"))).AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Delete task"))));
            TestApp.Screenshot(w, "e2e-aiapply-tasks.png");

            // …and a reminder → it must show on the Reminders page.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToHome"))).AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("reminders"))).AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName("Delete reminder"))));
            TestApp.Screenshot(w, "e2e-aiapply-reminders.png");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_PROVIDER", null);
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
            if ((el?.Name ?? "").Contains(contains, StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(250);
        }
        throw new TimeoutException($"'{automationId}' never contained '{contains}'.");
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
