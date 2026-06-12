using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Microsoft.Data.Sqlite;

namespace CacheNote.UiTests;

/// <summary>
/// Regression coverage for the 2026-06 font-color/list bug sweep (the app is dark-mode only):
///  1. spectrum picker commits its color and the editor keeps painting (no blank text),
///  2. circle-list items insert inline AT the caret and Enter continues the list,
///  3. bullet/number/circle list tools are mutually exclusive via disabled buttons
///     (screenshots fontcolor-*.png are the visual record).
/// </summary>
public sealed class E2E_FontColorAndTheme
{
    [Fact]
    public void SpectrumColor_CircleList_ExclusiveLists()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorBody")));
            w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))!.AsButton().Invoke();
            Pause();
            var title = w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))!.AsTextBox();
            title.Text = "fontcolor";
            Pause();
            var body = w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!;
            body.Click();
            Thread.Sleep(400);
            Keyboard.Type("spectrumfix");
            Thread.Sleep(800);
            SelectAll();

            // --- 1. spectrum pick commits + editor keeps painting ---
            // The whole suite shares one data dir — start from a clean event log so the
            // dismiss detection below can't match a CLOSED line from an earlier test.
            var diagLog = Path.Combine(Environment.GetEnvironmentVariable("CacheNote_DATA_DIR")!, "color-diag.log");
            if (File.Exists(diagLog))
                File.Delete(diagLog);

            var colorBtn = w.FindFirstDescendant(c => c.ByAutomationId("ColorButton"))!;
            var btnRect = colorBtn.BoundingRectangle;
            Mouse.Click(new System.Drawing.Point(btnRect.Left + btnRect.Width / 2, btnRect.Top + btnRect.Height / 2));
            Thread.Sleep(900);
            // Swatches are the only small (~32px) ListItems; notes-list rows are ~270px wide.
            var swatches = WaitFor(() =>
            {
                var items = w.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))
                    .Where(i => i.BoundingRectangle.Width > 0 && i.BoundingRectangle.Width < 60)
                    .ToArray();
                return items.Length >= 9 ? items : null;
            }, 10);
            var firstRect = swatches[0].BoundingRectangle;
            // Click into the spectrum box below the swatches; geometry varies a bit per run,
            // so retry rightwards until the app logs a COLORCHANGED event.
            foreach (var dx in new[] { 40, 90, 140 })
            {
                Mouse.Click(new System.Drawing.Point(firstRect.Left + dx, firstRect.Bottom + 150));
                Thread.Sleep(800);
                if (File.Exists(diagLog) && File.ReadAllText(diagLog).Contains("COLORCHANGED"))
                    break;
            }

            // Dismiss with real clicks on the editor (ESC is flaky against windowed popups).
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var b = w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!.BoundingRectangle;
                Mouse.Click(new System.Drawing.Point(b.Left + 40, b.Top + 15));
                Thread.Sleep(900);
                if (File.Exists(diagLog) && File.ReadAllText(diagLog).Contains("CLOSED"))
                    break;
            }
            Thread.Sleep(1500);

            // The app logs "COMMITTED picked=#AARRGGBB readback=#AARRGGBB" — assert the commit
            // stuck and that exactly that color reached the persisted RTF.
            var committed = File.ReadAllLines(diagLog).Last(l => l.Contains("COMMITTED"));
            var m = System.Text.RegularExpressions.Regex.Match(committed, @"picked=#(?<a>..)(?<r>..)(?<g>..)(?<b>..) readback=#(?<a2>..)(?<r2>..)(?<g2>..)(?<b2>..)");
            Assert.True(m.Success, $"no parsable commit in: {committed}");
            Assert.Equal(m.Groups["r"].Value + m.Groups["g"].Value + m.Groups["b"].Value,
                         m.Groups["r2"].Value + m.Groups["g2"].Value + m.Groups["b2"].Value);   // readback == picked
            var token = $"\\red{Convert.ToInt32(m.Groups["r"].Value, 16)}\\green{Convert.ToInt32(m.Groups["g"].Value, 16)}\\blue{Convert.ToInt32(m.Groups["b"].Value, 16)}";
            var spectrumToken = token;
            var rtf = ReadRtf("fontcolor");
            Assert.Contains(spectrumToken, rtf);   // the spectrum color reached the document
            TestApp.Screenshot(w, "fontcolor-1-spectrum.png");

            // --- 2. circle list inserts at the caret, Enter continues ---
            body = w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!;
            body.Click();
            Thread.Sleep(300);
            Keyboard.Type(VirtualKeyShort.END);
            Keyboard.Type(VirtualKeyShort.ENTER);
            Thread.Sleep(300);
            w.FindFirstDescendant(c => c.ByAutomationId("AddChecklistTool"))!.AsButton().Invoke();
            Thread.Sleep(400);
            Keyboard.Type("item one");
            Keyboard.Type(VirtualKeyShort.ENTER);
            Thread.Sleep(300);
            Keyboard.Type("item two");
            Thread.Sleep(1500);
            var plain = ReadPlain("fontcolor");
            Assert.Contains("○  item one", plain);
            Assert.Contains("○  item two", plain);
            TestApp.Screenshot(w, "fontcolor-2-circle-list.png");

            // --- 2b. list types are mutually exclusive via DISABLED buttons (owner request) ---
            // Caret sits at the end of "item two" (a circle line): bullet/number are disabled.
            var bulletBtn = w.FindFirstDescendant(c => c.ByAutomationId("BulletButton"))!;
            var numberBtn = w.FindFirstDescendant(c => c.ByAutomationId("NumberButton"))!;
            var circleBtn = w.FindFirstDescendant(c => c.ByAutomationId("AddChecklistTool"))!;
            Assert.False(bulletBtn.Properties.IsEnabled.ValueOrDefault, "bullets must be disabled inside a circle list");
            Assert.False(numberBtn.Properties.IsEnabled.ValueOrDefault, "numbering must be disabled inside a circle list");
            Assert.True(circleBtn.Properties.IsEnabled.ValueOrDefault, "circle stays enabled (to toggle off)");

            // Move the caret to the plain first line: bullets re-enable; making it a bullet
            // list then disables the circle tool.
            var bodyRect = w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!.BoundingRectangle;
            Mouse.Click(new System.Drawing.Point(bodyRect.Left + 40, bodyRect.Top + 15));
            Thread.Sleep(800);
            Assert.True(bulletBtn.Properties.IsEnabled.ValueOrDefault, "bullets enabled on a plain line");
            bulletBtn.AsToggleButton().Click();
            Thread.Sleep(800);
            Assert.False(circleBtn.Properties.IsEnabled.ValueOrDefault, "circle must be disabled inside a bullet list");
            plain = ReadPlain("fontcolor");
            Assert.Contains("○  item one", plain);   // circle lines untouched by the bullet toggle
            Assert.Contains("○  item two", plain);
            TestApp.Screenshot(w, "fontcolor-2b-exclusive.png");
            bulletBtn.AsToggleButton().Click();   // back off the bullet
            Thread.Sleep(600);

            // --- 3. dark-mode only: no theme toggle, and the custom color persists ---
            Assert.Null(w.FindFirstDescendant(c => c.ByAutomationId("ThemeToggle")));
            Assert.Contains(spectrumToken, ReadRtf("fontcolor"));
            TestApp.Screenshot(w, "fontcolor-3-final.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static void SelectAll()
    {
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.KEY_A);
        Thread.Sleep(300);
    }

    private static string ReadRtf(string title)
    {
        using var r = Query(title);
        return r.Read() && !r.IsDBNull(0) ? System.Text.Encoding.UTF8.GetString((byte[])r[0]) : "";
    }

    private static string ReadPlain(string title)
    {
        using var r = Query(title, "content_plain");
        return r.Read() && !r.IsDBNull(0) ? r.GetString(0) : "";
    }

    private static SqliteDataReader Query(string title, string column = "content_rtf")
    {
        var db = Path.Combine(Environment.GetEnvironmentVariable("CacheNote_DATA_DIR")!, "CacheNote.db");
        var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM notes WHERE title = @t ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@t", title);
        return cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
    }

    private static void Pause() => Thread.Sleep(450);

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
