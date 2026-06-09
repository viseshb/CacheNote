using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// Helpers shared by the per-milestone FlaUI smoke tests: locate the freshly
/// built CacheNote.App.exe, launch it, get its main window, and capture
/// screenshots into the solution's <c>artifacts/</c> folder for the review gate.
/// </summary>
internal static class TestApp
{
    // The harness launches the exe repeatedly and needs a fresh process each time, so it
    // bypasses single-instance redirection. Child processes inherit this environment var.
    static TestApp()
    {
        Environment.SetEnvironmentVariable("CacheNote_NO_SINGLE_INSTANCE", "1");
        // Keep the startup update check (and its blocking "Update available" dialog) out of UI runs.
        Environment.SetEnvironmentVariable("CacheNote_NO_UPDATE_CHECK", "1");
        // Isolate the test database so UI tests never pollute the user's real app data. One dir per
        // run (set once) so restart-persistence tests still share a DB across relaunches.
        Environment.SetEnvironmentVariable("CacheNote_DATA_DIR",
            Path.Combine(Path.GetTempPath(), "CacheNote-uitests", Guid.NewGuid().ToString("N")));
    }

    public static string FindExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CacheNote_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var slnDir = FindSolutionDir();
        var appBin = Path.Combine(slnDir, "CacheNote.App", "bin");
        if (!Directory.Exists(appBin))
            throw new FileNotFoundException($"App bin folder not found at {appBin}. Build CacheNote.App first.");

        var all = Directory.GetFiles(appBin, "CacheNote.App.exe", SearchOption.AllDirectories);
        // Prefer the Debug win-x64 build (the one we run for the user) so tests + app stay in lockstep.
        var exe = all.FirstOrDefault(p => p.Contains("win-x64") && p.Contains("Debug"))
            ?? all.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

        return exe ?? throw new FileNotFoundException("CacheNote.App.exe not found. Build CacheNote.App first.");
    }

    public static string ArtifactsDir()
    {
        var dir = Path.Combine(FindSolutionDir(), "artifacts");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static Window WaitForMainWindow(Application app, UIA3Automation automation, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(2));
            if (window is not null && !string.IsNullOrEmpty(window.Title))
                return window;
            Thread.Sleep(300);
        }

        throw new TimeoutException($"Main window did not appear within {timeoutSeconds}s.");
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private struct RECT { public int Left, Top, Right, Bottom; }

    public static void Screenshot(AutomationElement element, string fileName)
    {
        try { element.AsWindow()?.SetForeground(); } catch { /* best effort */ }
        Thread.Sleep(300);

        // PrintWindow on the top-level HWND captures the window's own surface,
        // reliable for WinUI/DComp even when occluded by other windows.
        var hwnd = new IntPtr(element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());
        GetWindowRect(hwnd, out var r);
        var w = Math.Max(1, r.Right - r.Left);
        var h = Math.Max(1, r.Bottom - r.Top);

        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
            g.ReleaseHdc(hdc);
        }
        bmp.Save(Path.Combine(ArtifactsDir(), fileName));
    }

    /// <summary>
    /// Captures the actual on-screen pixels of the window rect (BitBlt from screen).
    /// Unlike PrintWindow, this preserves transient input states like hover — at the
    /// cost of needing the window to be foreground and unobscured.
    /// </summary>
    public static void ScreenshotScreen(AutomationElement element, string fileName)
    {
        var hwnd = new IntPtr(element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64());
        GetWindowRect(hwnd, out var r);
        var w = Math.Max(1, r.Right - r.Left);
        var h = Math.Max(1, r.Bottom - r.Top);

        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
        bmp.Save(Path.Combine(ArtifactsDir(), fileName));
    }

    private static string FindSolutionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CacheNote.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate CacheNote.sln above the test directory.");
    }
}
