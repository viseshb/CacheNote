using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace CacheNote.UiTests;

/// <summary>
/// Behavioral coverage for Notes features that the other smokes only assert presence for:
/// the {} markdown composer actually renders, search actually filters, a tag actually attaches,
/// and archive actually removes a note from the list.
/// </summary>
public sealed class E2E_NotesFeatures
{
    [Fact]
    public void Markdown_Search_Tags_Archive_Behave()
    {
        var g = Guid.NewGuid().ToString("N")[..6];
        var exe = TestApp.FindExe();
        using var automation = new UIA3Automation();
        var app = Application.Launch(exe);
        try
        {
            var w = TestApp.WaitForMainWindow(app, automation);
            w.FindFirstDescendant(c => c.ByAutomationId("notes"))!.AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorBody")));

            // ---- Markdown {} composer renders into the note (composer shows, Preview dismisses it) ----
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))).AsButton().Invoke();
            Thread.Sleep(400);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddMarkdownTool"))).AsButton().Invoke();
            var mdBox = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("MdComposerBox"))).AsTextBox();
            mdBox.Text = "# Heading\n- bullet one\n- bullet two";
            Thread.Sleep(200);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("MdRenderButton"))).AsButton().Invoke();
            Thread.Sleep(500);
            Assert.Null(w.FindFirstDescendant(c => c.ByAutomationId("MdComposerBox")));   // composer gone => rendered
            TestApp.Screenshot(w, "nf-markdown.png");

            // ---- Tags: add a tag, confirm a chip with that name appears ----
            var tagName = "tag" + g;
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("AddTagButton"))).AsButton().Invoke();
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewTagBox"))).AsTextBox().Text = tagName;
            Thread.Sleep(150);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("ConfirmAddTag"))).AsButton().Invoke();
            Thread.Sleep(400);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(tagName))));   // tag chip
            TestApp.Screenshot(w, "nf-tags.png");

            // ---- Archive: create a uniquely-named note, archive it, confirm it leaves the list ----
            var archTitle = "Arch" + g;
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))).AsButton().Invoke();
            Thread.Sleep(300);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))).AsTextBox().Text = archTitle;
            Thread.Sleep(1000);   // autosave so the row shows the title
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(archTitle))));   // present before archive
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Note actions"))).AsButton().Invoke();   // top row = this note
            Thread.Sleep(300);
            WaitFor(() => w.FindFirstDescendant(c => c.ByName("Archive"))).Click();
            Thread.Sleep(700);
            Assert.Null(w.FindFirstDescendant(c => c.ByName(archTitle)));   // gone after archive
            TestApp.Screenshot(w, "nf-archive.png");

            // ---- Search filters: two titled notes, search one token, only it remains ----
            var aTitle = "Apple" + g;
            var bTitle = "Banana" + g;
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))).AsButton().Invoke();
            Thread.Sleep(300);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))).AsTextBox().Text = aTitle;
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NewNoteButton"))).AsButton().Invoke();
            Thread.Sleep(300);
            WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("EditorTitle"))).AsTextBox().Text = bTitle;
            Thread.Sleep(1300);   // let autosave commit both titles to FTS

            // Focus the AutoSuggestBox's inner edit so typing registers as real user input (the filter
            // ignores programmatic text changes).
            var search = WaitFor(() => w.FindFirstDescendant(c => c.ByAutomationId("NotesSearch")));
            var edit = search.FindFirstDescendant(c => c.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)) ?? search;
            edit.Click();
            Thread.Sleep(200);
            Keyboard.Type(aTitle);
            Thread.Sleep(1000);
            Assert.NotNull(WaitFor(() => w.FindFirstDescendant(c => c.ByName(aTitle))));      // match shown
            Assert.Null(w.FindFirstDescendant(c => c.ByName(bTitle)));                        // non-match filtered out
            TestApp.Screenshot(w, "nf-search.png");
        }
        finally
        {
            try { app.Close(); } catch { }
            if (!app.HasExited) { try { app.Kill(); } catch { } }
        }
    }

    private static T WaitFor<T>(Func<T?> get, int sec = 10) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(sec);
        while (DateTime.UtcNow < deadline) { var v = get(); if (v is not null) return v; Thread.Sleep(250); }
        throw new TimeoutException("Element did not appear in time.");
    }
}
