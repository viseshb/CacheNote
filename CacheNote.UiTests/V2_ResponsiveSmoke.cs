using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// V2: at a narrow window width the app must not overlap. Settings rows reflow to two columns,
/// the Notes header collapses to icons, and Notes becomes a single-pane master-detail
/// (list → tap a note → editor full-width + back). Captures screenshots for the review gate.
/// </summary>
public sealed class V2_ResponsiveSmoke
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static void Resize(Window w, int width, int height)
    {
        var hwnd = new IntPtr(w.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());
        SetWindowPos(hwnd, IntPtr.Zero, 80, 40, width, height, 0x0004 /* SWP_NOZORDER */);
        Thread.Sleep(700);
    }

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
    public void NarrowWidth_NoOverlap_Settings_And_Notes_MasterDetail()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            Resize(w, 520, 840);     // clearly below the 640-DIP compact breakpoint
            TestApp.Screenshot(w, "v2-01-home-narrow.png");

            // --- Settings: rows must reflow (label left, control right), no overlap ---
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("settings")))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("FontSizeSetting"))));
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("TestAiButton"))));
            TestApp.Screenshot(w, "v2-02-settings-narrow.png");
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToHome")))!.AsButton().Invoke();

            // --- Notes: compact list, then tap a note → detail with back + Tools dropdown ---
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("notes")))!.AsButton().Invoke();
            var list = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NotesList")));
            Assert.NotNull(list);
            TestApp.Screenshot(w, "v2-03-notes-list-narrow.png");

            // Fresh DB has one pre-selected note — tapping it must open detail (regression: single-row list).
            var loneRow = WaitFor(() => w.FindFirstDescendant(c => c.ByName("Untitled")));
            Assert.NotNull(loneRow);
            loneRow!.Click();
            Thread.Sleep(500);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToList"))));
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))));
            TestApp.Screenshot(w, "v2-03b-single-note-opens.png");
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToList")))!.AsButton().Invoke();

            // Create a note so there's something to open even on a clean DB.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton")))!.AsButton().Invoke();
            Thread.Sleep(500);

            // In detail the back-to-list button and the collapsed Tools button must be present.
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToList"))));
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorToolsButton"))));
            TestApp.Screenshot(w, "v2-04-notes-detail-narrow.png");

            // Open the Tools dropdown → a real tool (Bold) lives inside it.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorToolsButton")))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BoldButton"))));
            TestApp.Screenshot(w, "v2-05-notes-tools-flyout.png");
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Thread.Sleep(300);

            // Back → list again.
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToList")))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NotesList"))));
            TestApp.Screenshot(w, "v2-06-notes-list-again.png");

            // --- Wide again: two-pane returns ---
            Resize(w, 1280, 820);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("ToggleListButton"))));
            TestApp.Screenshot(w, "v2-07-notes-wide.png");

            // --- Centering on wide screens: Tasks + Settings content should sit centered ---
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToHome")))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("tasks")))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddTask"))));
            TestApp.Screenshot(w, "v2-08-tasks-wide.png");
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToHome")))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("settings")))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("FontSizeSetting"))));
            TestApp.Screenshot(w, "v2-09-settings-wide.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }
}
