using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace StickyDesk.UiTests;

/// <summary>
/// M4 gate smoke: Tasks CRUD (create / complete / delete), plus a navigation smoke that the
/// Favorites section and the Notes search box + tag affordances are present.
/// </summary>
public sealed class M4_TasksSearchTags
{
    [Fact]
    public void Tasks_Create_Complete_Delete()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            // Hub → Tasks.
            w.FindFirstDescendant(c => c.ByAutomationId("tasks"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddTask"))?.AsButton());

            ClearAllTasks(w);

            var title = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("TaskTitle"))?.AsTextBox());
            title.Text = "Buy groceries";
            Thread.Sleep(150);

            w.FindFirstDescendant(c => c.ByAutomationId("AddTask"))!.AsButton().Invoke();
            Thread.Sleep(500);

            var del = WaitFor(() => w.FindFirstDescendant(c => c.ByName("Delete task"))?.AsButton());
            Assert.NotNull(del);
            TestApp.Screenshot(w, "m4-tasks.png");

            // Complete it (checkbox), then delete it.
            var complete = WaitFor(() => w.FindFirstDescendant(c => c.ByName("Complete task"))?.AsCheckBox());
            complete.Click();
            Thread.Sleep(400);

            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Delete task"))?.AsButton())!.Invoke();
            Thread.Sleep(500);
            Assert.Null(w.FindFirstDescendant(c => c.ByName("Delete task")));
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    [Fact]
    public void Notes_Search_And_Favorites_Present()
    {
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);

            // Notes section: search box + tag add affordance present.
            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NotesSearch"))));
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddTagButton"))));
            Assert.NotNull(w.FindFirstDescendant(c => c.ByAutomationId("RemindButton")));

            // Back to hub, then Favorites loads.
            w.FindFirstDescendant(c => c.ByAutomationId("BackToHome"))!.AsButton().Invoke();
            var fav = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("favorites"))?.AsButton());
            fav.Invoke();
            // The favorites list (ItemsRepeater) is empty by default and won't surface its id;
            // assert the page header instead, which is always present.
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("FavoritesTitle"))));
            TestApp.Screenshot(w, "m4-favorites.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static void ClearAllTasks(Window w)
    {
        for (var i = 0; i < 25; i++)
        {
            var del = w.FindFirstDescendant(c => c.ByName("Delete task"))?.AsButton();
            if (del is null)
                return;
            del.Invoke();
            Thread.Sleep(300);
        }
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
