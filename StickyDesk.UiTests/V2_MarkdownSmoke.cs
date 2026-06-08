using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace StickyDesk.UiTests;

/// <summary>
/// V2 Markdown: the editor {} tool inserts a Markdown block at the caret; the block edits as
/// monospace source and renders via the Preview toggle. Screenshots for the review gate.
/// </summary>
public sealed class V2_MarkdownSmoke
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
    public void MarkdownBlock_InsertEditPreview()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("notes")))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton")))!.AsButton().Invoke();
            Thread.Sleep(400);

            // Insert a Markdown block via the {} tool.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddMarkdownTool")))!.AsButton().Invoke();
            var src = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("MdSourceBox")));
            Assert.NotNull(src);
            src!.AsTextBox().Enter("# Heading with **bold** and `code`");
            Thread.Sleep(300);
            TestApp.Screenshot(w, "v2-md-01-source.png");

            // Toggle Preview → source hides, rendered markdown shows.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("MdPreviewToggle")))!.AsToggleButton().Toggle();
            Thread.Sleep(500);
            // In preview the editable source box is collapsed (so not in the UIA tree).
            Assert.Null(w.FindFirstDescendant(c => c.ByAutomationId("MdSourceBox")));
            TestApp.Screenshot(w, "v2-md-02-preview.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }
}
