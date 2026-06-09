using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// End-to-end lifecycle: the keystone "create → close the exe → relaunch → data is still there"
/// round-trip for Notes (title + body via FTS) and Tasks, plus a resize sweep that the layout
/// survives every breakpoint and the hard minimum window size is enforced.
/// </summary>
public sealed class E2E_Lifecycle
{
    // ---------- Notes persist across a real restart ----------
    [Fact]
    public void Notes_Title_And_Body_Persist_Across_Restart()
    {
        var exe = TestApp.FindExe();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var title = "T" + marker;
        var bodyToken = "B" + marker;   // unique single FTS token

        using var automation = new UIA3Automation();

        // session 1 — create + let autosave commit
        var app1 = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app1, automation);
            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))).AsButton().Invoke();
            Thread.Sleep(500);

            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))?.AsTextBox()).Text = title;

            var body = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorBody")));
            body.Click();
            Thread.Sleep(150);
            Keyboard.Type(bodyToken);
            Thread.Sleep(1500);   // > 600ms debounce → SaveContent persists title + body to SQLite
        }
        finally { HardExit(app1); }

        // session 2 — verify it survived the restart
        var app2 = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app2, automation);
            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();

            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(title))));   // title row survived
            TestApp.Screenshot(w, "e2e-notes-persist-title.png");

            // Body survived: searching the unique token (FTS) must surface the note.
            var search = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NotesSearch")));
            search.Click();
            Thread.Sleep(200);
            Keyboard.Type(bodyToken);
            Thread.Sleep(900);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(title))));
            TestApp.Screenshot(w, "e2e-notes-persist-body.png");
        }
        finally { HardExit(app2); }
    }

    // ---------- Tasks persist across a real restart ----------
    [Fact]
    public void Tasks_Persist_Across_Restart()
    {
        var exe = TestApp.FindExe();
        var taskTitle = "Task " + Guid.NewGuid().ToString("N")[..8];

        using var automation = new UIA3Automation();

        var app1 = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app1, automation);
            w.FindFirstDescendant(c => c.ByAutomationId("tasks"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("TaskTitle"))?.AsTextBox()).Text = taskTitle;
            Thread.Sleep(150);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddTask"))).AsButton().Invoke();
            Thread.Sleep(500);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(taskTitle))));   // created
        }
        finally { HardExit(app1); }

        var app2 = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app2, automation);
            w.FindFirstDescendant(c => c.ByAutomationId("tasks"))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(taskTitle))));   // survived restart
            TestApp.Screenshot(w, "e2e-tasks-persist.png");

            // Clean up: delete it so the shared DB doesn't accumulate test rows.
            var del = w.FindFirstDescendant(c => c.ByName("Delete task"))?.AsButton();
            del?.Invoke();
        }
        finally { HardExit(app2); }
    }

    // ---------- Resize sweep: no crash at any breakpoint, min size enforced ----------
    [Fact]
    public void Resize_Sweep_NoCrash_And_MinSize_Enforced()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            foreach (var (cw, ch) in new[] { (1920, 1080), (1000, 720), (640, 760), (380, 560) })
            {
                Resize(w, cw, ch);
                Assert.False(app.HasExited, $"app exited resizing to {cw}x{ch}");
                Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("notes"))));
                TestApp.Screenshot(w, $"e2e-resize-{cw}.png");
            }

            // Below the hard floor → Windows clamps up to PreferredMinimumWidth.
            Resize(w, 200, 300);
            GetWindowRect(Hwnd(w), out var r);
            Assert.True(r.Right - r.Left >= 360, $"min width not enforced (got {r.Right - r.Left}px)");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    // ---------- AI ball panel must fit within the window at the minimum width ----------
    [Fact]
    public void AiBall_Panel_Fits_Within_Window_At_MinWidth()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            Resize(w, 380, 560);

            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiBall"))).AsButton().Invoke();
            // The Border panel has no UIA peer; assert on its child controls (the input row spans the
            // panel width), which is what actually matters: usable controls must be fully on-screen.
            var input = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatInput")));
            var send = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AiChatSend")));
            Thread.Sleep(700);   // let the open animation settle to full size

            var ir = input.BoundingRectangle;
            var sr = send.BoundingRectangle;
            var win = w.BoundingRectangle;
            Assert.True(ir.Left >= win.Left - 2, $"AI input clips the left edge (input {ir.Left} < window {win.Left})");
            Assert.True(sr.Right <= win.Right + 2, $"AI send button overflows the right edge (send {sr.Right} > window {win.Right})");
            Assert.True(sr.Bottom <= win.Bottom + 2, $"AI input row overflows the bottom (row {sr.Bottom} > window {win.Bottom})");
            TestApp.Screenshot(w, "e2e-aiball-narrow.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    // ---------- helpers ----------
    private static void HardExit(Application app)
    {
        try { app.Kill(); } catch { /* may already be gone */ }
        var sw = Stopwatch.StartNew();
        while (!app.HasExited && sw.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(100);
        Thread.Sleep(900);   // let SQLite release the DB file lock before the next launch
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    private struct RECT { public int Left, Top, Right, Bottom; }

    private static IntPtr Hwnd(Window w) => new(w.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());

    private static void Resize(Window w, int width, int height)
    {
        SetWindowPos(Hwnd(w), IntPtr.Zero, 60, 30, width, height, 0x0004 /* SWP_NOZORDER */);
        Thread.Sleep(800);
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
