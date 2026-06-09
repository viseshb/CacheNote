using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// AI note-context actions via the ball (fake provider): Summarize inserts a summary into the open
/// note; Rephrase with no selection shows its guard message. (Live AI is covered by AiLiveTests.)
/// </summary>
public sealed class E2E_AiActions
{
    [Fact]
    public void Summarize_Inserts_And_Rephrase_Guards()
    {
        Environment.SetEnvironmentVariable("AI_PROVIDER", "fake");
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))).AsButton().Invoke();
            Thread.Sleep(400);
            var body = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorBody")));
            body.Click();
            Thread.Sleep(150);
            Keyboard.Type("The quarterly report covers revenue, churn, and a hiring plan.");
            Thread.Sleep(800);

            // Open the ball and summarize the open note.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiBall"))).AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiSummarize"))).AsButton().Invoke();
            WaitForText(w, "AiStatus", "Summary");
            TestApp.Screenshot(w, "ai-summarize.png");

            // Rephrase with nothing selected → guard message.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiRephrase"))).AsButton().Invoke();
            WaitForText(w, "AiStatus", "select");
            TestApp.Screenshot(w, "ai-rephrase-guard.png");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_PROVIDER", null);
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
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
