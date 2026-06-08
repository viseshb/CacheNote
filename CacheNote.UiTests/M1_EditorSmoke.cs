using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// M1b gate smoke: app opens to the home hub (cards); clicking Notes navigates to
/// the editor (title/body/checklist/active-state toolbar); the theme toggle flips.
/// </summary>
public sealed class M1_EditorSmoke
{
    [Fact]
    public void Home_Hub_Navigates_To_Notes_Editor()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var window = TestApp.WaitForMainWindow(app, automation);

            // Home hub shows feature cards.
            var notesCard = window.FindFirstDescendant(cf => cf.ByAutomationId("notes"))?.AsButton();
            var remindersCard = window.FindFirstDescendant(cf => cf.ByAutomationId("reminders"));
            Assert.NotNull(notesCard);
            Assert.NotNull(remindersCard);
            TestApp.Screenshot(window, "m1b-home.png");

            // Enter the Notes section.
            notesCard!.Invoke();

            var title = WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("EditorTitle"))?.AsTextBox());
            var body = window.FindFirstDescendant(cf => cf.ByAutomationId("EditorBody"));
            var addItem = window.FindFirstDescendant(cf => cf.ByAutomationId("AddChecklistTool"))?.AsButton();
            var bold = window.FindFirstDescendant(cf => cf.ByAutomationId("BoldButton"));
            Assert.NotNull(title);
            Assert.NotNull(body);
            Assert.NotNull(addItem);
            Assert.NotNull(bold);

            title!.Text = "Groceries";
            Assert.Equal("Groceries", title.Text);
            addItem!.Invoke();

            // Create a second note → the list shows multiple notes.
            var newBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("NewNoteButton"))?.AsButton();
            Assert.NotNull(newBtn);
            newBtn!.Invoke();
            var title2 = WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("EditorTitle"))?.AsTextBox());
            title2!.Text = "Trip plan";

            var notesList = window.FindFirstDescendant(cf => cf.ByAutomationId("NotesList"));
            Assert.NotNull(notesList);
            TestApp.Screenshot(window, "m1b-notes.png");

            // Visible "⋯" actions menu can delete a note (no right-click needed).
            var before = notesList!.FindAllChildren().Length;
            var actionsBtn = window.FindFirstDescendant(cf => cf.ByName("Note actions"))?.AsButton();
            Assert.NotNull(actionsBtn);
            actionsBtn!.Invoke();
            var deleteItem = WaitFor(() => window.FindFirstDescendant(cf => cf.ByName("Delete")));
            deleteItem.Click();
            System.Threading.Thread.Sleep(500);
            var after = window.FindFirstDescendant(cf => cf.ByAutomationId("NotesList"))!.FindAllChildren().Length;
            Assert.True(after < before, $"Delete should reduce note count (before={before}, after={after}).");

            // Theme toggle flips without crashing.
            var themeToggle = window.FindFirstDescendant(cf => cf.ByAutomationId("ThemeToggle"))?.AsButton();
            Assert.NotNull(themeToggle);
            themeToggle!.Invoke();
            System.Threading.Thread.Sleep(400);
            TestApp.Screenshot(window, "m1b-notes-toggled.png");

            // Back to home.
            var back = window.FindFirstDescendant(cf => cf.ByAutomationId("BackToHome"))?.AsButton();
            Assert.NotNull(back);
            back!.Invoke();
        }
        finally
        {
            try { app.Close(); } catch { /* ignore */ }
            if (!app.HasExited)
            {
                try { app.Kill(); } catch { /* ignore */ }
            }
        }
    }

    private static T WaitFor<T>(Func<T?> get, int timeoutSeconds = 8) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var v = get();
            if (v is not null)
                return v;
            System.Threading.Thread.Sleep(250);
        }
        throw new TimeoutException("Element did not appear in time.");
    }
}
