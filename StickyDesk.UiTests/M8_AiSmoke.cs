using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace StickyDesk.UiTests;

/// <summary>
/// M8 gate smoke (fake provider): AI_PROVIDER=fake → open the AI assistant, plan from an
/// instruction (preview), then Apply, which creates content through the real repositories.
/// The live Gemini/Vertex path needs a key (manual check).
/// </summary>
public sealed class M8_AiSmoke
{
    [Fact]
    public void Fake_Plan_Then_Apply()
    {
        Environment.SetEnvironmentVariable("AI_PROVIDER", "fake");
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiButton"))).AsButton().Invoke();

            var instr = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiInstruction"))?.AsTextBox());
            instr.Text = "plan my product launch";
            Thread.Sleep(150);

            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiPlan"))).AsButton().Invoke();
            WaitForText(w, "AiStatus", "Planned");
            TestApp.Screenshot(w, "m8-ai-plan.png");

            // ContentDialog primary button is "Apply".
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Apply"))).AsButton().Invoke();
            WaitForText(w, "AiStatus", "Applied");
            TestApp.Screenshot(w, "m8-ai-applied.png");

            w.FindFirstDescendant(c => c.ByName("Close"))?.AsButton().Invoke();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_PROVIDER", null);
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static void WaitForText(Window w, string automationId, string contains, int timeoutSeconds = 8)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var el = w.FindFirstDescendant(c => c.ByAutomationId(automationId));
            var text = el?.Name ?? "";
            if (text.Contains(contains, StringComparison.OrdinalIgnoreCase))
                return;
            Thread.Sleep(250);
        }
        throw new TimeoutException($"'{automationId}' never contained '{contains}'.");
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
