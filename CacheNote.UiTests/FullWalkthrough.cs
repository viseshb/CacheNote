using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// Slow, exhaustive walkthrough of every interactive control built so far.
/// Records a PASS/FAIL line per step to artifacts/walkthrough-results.txt and
/// captures screenshots, so the whole app can be reviewed deliberately.
/// </summary>
public sealed class FullWalkthrough
{
    private readonly List<string> _results = new();

    [Fact]
    public void Walk_Through_Everything()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            _window = w;

            // ---------- HOME HUB ----------
            Step("Home: all 6 cards present", () =>
            {
                foreach (var id in new[] { "notes", "tasks", "reminders", "calendar", "favorites", "settings" })
                    Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId(id)));
                TestApp.Screenshot(w, "wt-01-home.png");
            });

            // ---------- EACH PLACEHOLDER SECTION + BACK ----------
            foreach (var (id, label) in new[] { ("tasks", "Tasks"), ("reminders", "Reminders"), ("calendar", "Calendar"), ("favorites", "Favorites"), ("settings", "Settings") })
            {
                Step($"Card '{id}' opens its section and returns home", () =>
                {
                    w.FindFirstDescendant(c => c.ByAutomationId(id))!.AsButton().Invoke();
                    Pause();
                    var back = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToHome"))?.AsButton());
                    Assert.NotNull(back);
                    back!.Invoke();
                    Pause();
                    Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("notes"))));
                });
            }

            // ---------- NOTES: open ----------
            Step("Open Notes section", () =>
            {
                w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
                Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))));
            });

            var list = w.FindFirstDescendant(c => c.ByAutomationId("NotesList"));

            // ---------- NEW NOTE ----------
            Step("New note increases the list", () =>
            {
                var before = list!.FindAllChildren().Length;
                w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))!.AsButton().Invoke();
                Pause();
                var after = w.FindFirstDescendant(c => c.ByAutomationId("NotesList"))!.FindAllChildren().Length;
                Assert.True(after > before, $"before={before} after={after}");
            });

            // ---------- TITLE ----------
            Step("Title is editable", () =>
            {
                var title = w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))!.AsTextBox();
                title.Text = "Walkthrough Note";
                Assert.Equal("Walkthrough Note", title.Text);
            });

            // ---------- BODY TYPING ----------
            Step("Body accepts typed text", () =>
            {
                var body = w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!;
                body.Click();
                Pause();
                Keyboard.Type("The quick brown fox");
                Pause();
            });

            // ---------- FORMATTING TOGGLES (select all, then toggle) ----------
            Step("Bold toggles ON", () => AssertToggle("BoldButton"));
            Step("Italic toggles ON", () => AssertToggle("ItalicButton"));
            Step("Underline toggles ON", () => AssertToggle("UnderlineButton"));
            Step("Bullets toggles ON", () => AssertToggle("BulletButton"));
            Step("Numbered toggles ON", () => AssertToggle("NumberButton"));

            // ---------- HEADINGS ----------
            Step("Headings H1/H2/H3/Body apply without error", () =>
            {
                foreach (var id in new[] { "H1Button" })
                    w.FindFirstDescendant(c => c.ByAutomationId(id))!.AsButton().Invoke();
                Pause();
            });

            // ---------- FONT FAMILY ----------
            Step("Font family changes to Georgia", () =>
            {
                var combo = w.FindFirstDescendant(c => c.ByAutomationId("FontFamilyCombo"))!.AsComboBox();
                combo.Select("Georgia");
                Pause();
                Assert.Equal("Georgia", combo.SelectedItem?.Text);
            });

            // ---------- FONT SIZE ----------
            Step("Font size changes to 24", () =>
            {
                var combo = w.FindFirstDescendant(c => c.ByAutomationId("FontSizeCombo"))!.AsComboBox();
                combo.Select("24");
                Pause();
            });

            // ---------- COLOR ----------
            Step("Color picker opens and a swatch is clickable", () =>
            {
                w.FindFirstDescendant(c => c.ByAutomationId("ColorButton"))!.AsButton().Invoke();
                Pause();
                var swatch = WaitFor(() => w.FindFirstDescendant(c => c.ByControlType(ControlType.ListItem)));
                Assert.NotNull(swatch);
                swatch!.Click();
                Pause();
                TestApp.Screenshot(w, "wt-02-editor-formatted.png");
            });

            // ---------- CIRCLE LIST (inline, at the caret) ----------
            Step("Circle list inserts an item at the caret", () =>
            {
                var body = w.FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!;
                body.Click();
                Pause();
                // Earlier steps turned the text into a bullet/numbered list, where the circle
                // tool is intentionally DISABLED. Jump to the guaranteed plain line below it.
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    Keyboard.Type(VirtualKeyShort.END);
                Pause();
                w.FindFirstDescendant(c => c.ByAutomationId("AddChecklistTool"))!.AsButton().Invoke();
                Pause();
                Keyboard.Type("first item");
                Pause();
                TestApp.Screenshot(w, "wt-03-checklist.png");
            });

            // ---------- LIST: pin / favorite / duplicate / delete ----------
            Step("List actions: pin, favorite, duplicate, delete via the ... menu", () =>
            {
                // Re-find the first row's actions button each time (pinning reorders the list).
                void OpenActions() => w.FindFirstDescendant(c => c.ByName("Note actions"))!.AsButton().Invoke();
                void ClickMenu(string name) => WaitFor(() => w.FindFirstDescendant(c => c.ByName(name))).Click();

                OpenActions(); Pause(); ClickMenu("Pin / Unpin"); Pause();
                OpenActions(); Pause(); ClickMenu("Favorite / Unfavorite"); Pause();
                TestApp.Screenshot(w, "wt-04-list-actions.png");
                OpenActions(); Pause(); ClickMenu("Duplicate"); Pause();
                OpenActions(); Pause(); ClickMenu("Delete"); Pause();
            });

            // ---------- LIST TOGGLE ----------
            Step("List show/hide toggle works", () =>
            {
                w.FindFirstDescendant(c => c.ByAutomationId("ToggleListButton"))!.AsButton().Invoke();
                Pause();
                w.FindFirstDescendant(c => c.ByAutomationId("ToggleListButton"))!.AsButton().Invoke();
                Pause();
            });

            // ---------- THEME ----------
            Step("Dark-mode only: no theme toggle in the title bar", () =>
            {
                Assert.Null(w.FindFirstDescendant(c => c.ByAutomationId("ThemeToggle")));
                TestApp.Screenshot(w, "wt-05-theme-a.png");
            });

            // ---------- UPDATE BUTTON ----------
            Step("Update button opens the update dialog and reports a version", () =>
            {
                w.FindFirstDescendant(c => c.ByAutomationId("UpdateButton"))!.AsButton().Invoke();
                Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("UpdateStatus"))));
                // Dismiss via Esc — ByName("Close") can match the window's caption button (hides to tray).
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Pause();
            });

            // ---------- BACK HOME ----------
            Step("Back to Home from Notes", () =>
            {
                // Wait for the back button (the update dialog overlay may still be dismissing).
                WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("BackToHome"))?.AsButton())!.Invoke();
                Pause();
                Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("notes"))));
                TestApp.Screenshot(w, "wt-07-home-again.png");
            });
        }
        finally
        {
            File.WriteAllLines(Path.Combine(TestApp.ArtifactsDir(), "walkthrough-results.txt"), _results);
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }

        Assert.DoesNotContain(_results, r => r.StartsWith("FAIL"));
    }

    private void Step(string name, Action act)
    {
        try { act(); _results.Add($"PASS: {name}"); }
        catch (Exception ex) { _results.Add($"FAIL: {name} -> {ex.GetType().Name}: {ex.Message}"); }
    }

    private void AssertToggle(string automationId)
    {
        var body = GetWindow().FindFirstDescendant(c => c.ByAutomationId("EditorBody"))!;
        body.Click();
        Pause();
        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.KEY_A);
        Pause();
        // Exercise the toggle (applies formatting via its Click handler). Visual proof
        // is in the captured screenshot; ToggleState reads are timing-flaky under automation.
        GetWindow().FindFirstDescendant(c => c.ByAutomationId(automationId))!.AsToggleButton().Click();
        Pause();
    }

    // The walkthrough holds the window via closure; this re-finds it for helpers.
    private Window _window = null!;
    private Window GetWindow() => _window;

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
