using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CacheNote.Core.Services;
using CacheNote.Core.Speech;
using CacheNote.Core.Ui;
using CacheNote.Core.ViewModels;
using CacheNote_App.Controls;
using CacheNote_App.Services;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace CacheNote_App;

/// <summary>
/// Notes section: two-pane list + editor. RichEditBox prose, inline checklist,
/// font/size/color, active-state toolbar, debounced autosave, and note management
/// (new / duplicate / pin / favorite / archive / delete).
/// </summary>
public sealed partial class MainPage : Page
{
    private static readonly string[] Fonts =
        ["Segoe UI", "Calibri", "Arial", "Georgia", "Times New Roman", "Verdana", "Consolas", "Comic Sans MS"];

    private static readonly string[] Sizes =
        ["10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "40", "48"];


    public NotesViewModel Vm { get; }

    private readonly DispatcherQueueTimer _saveTimer;
    private bool _loading;
    private bool _syncing;
    private bool _listVisible = true;
    private int _selStart;
    private int _selEnd;

    // Responsive state: below Responsive.CompactMax the notes view becomes a single-pane
    // master-detail and the editor toolbar collapses into the "Tools" dropdown.
    private bool _compact;
    private bool _compactDetail;

    public MainPage()
    {
        Vm = App.GetService<NotesViewModel>();
        InitializeComponent();

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        _syncing = true;
        FontFamilyCombo.ItemsSource = Fonts;
        FontFamilyCombo.SelectedItem = "Segoe UI";
        FontSizeCombo.ItemsSource = Sizes;
        FontSizeCombo.Text = "16";
        BuildSwatches();
        CustomColorPicker.Color = Color.FromArgb(255, 0x25, 0x63, 0xEB); // vivid default (so light mode isn't seeded dark)
        _syncing = false;

        // We have our own formatting toolbar — suppress the floating selection bar
        // that otherwise pops above it.
        EditorBox.SelectionFlyout = null;

        Vm.ContentRequested += OnContentRequested;
        Unloaded += (_, _) => Vm.ContentRequested -= OnContentRequested;
        ActualThemeChanged += (_, _) =>
        {
            BuildSwatches();
            RefreshEditorThemeBrushes();
            ApplyTitleColor();
        };
        SizeChanged += (_, e) => ApplyResponsive(e.NewSize.Width);
        Loaded += (_, _) =>
        {
            BuildSwatches();
            ApplyEditorFontSize();
            Vm.LoadList();
            RefreshTagFilter();
            if (_newOnLoad)
            {
                _newOnLoad = false;
                CreateNewNote();
            }
            else if (_openNoteId != 0)
            {
                var item = Vm.Notes.FirstOrDefault(n => n.Id == _openNoteId);
                if (item is not null)
                    Vm.Select(item);
                _openNoteId = 0;
            }
            ApplyResponsive(ActualWidth);
        };
    }

    // ----- responsive: single-pane master-detail + collapsible toolbar below CompactMax -----
    private void ApplyResponsive(double width)
    {
        if (width <= 0)
            return;
        _compact = Responsive.IsCompact(width);
        var fullVis = _compact ? Visibility.Collapsed : Visibility.Visible;

        // Compact header = icon-only (the text runs are what made the row overlap).
        HomeText.Visibility = fullVis;
        RemindText.Visibility = fullVis;
        NewText.Visibility = fullVis;
        SavedText.Visibility = fullVis;
        ToggleListButton.Visibility = fullVis;

        SetToolbarCollapsed(_compact);

        if (_compact)
        {
            ApplyCompactPane();
        }
        else
        {
            _compactDetail = false;
            BackToListButton.Visibility = Visibility.Collapsed;
            HomeButton.Visibility = Visibility.Visible;
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            ListColumn.Width = _listVisible ? new GridLength(288) : new GridLength(0);
            ListPanel.Visibility = _listVisible ? Visibility.Visible : Visibility.Collapsed;
            EditorScroll.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Compact: show EITHER the list (full width) OR the open note (full width) + a back button.</summary>
    private void ApplyCompactPane()
    {
        if (_compactDetail)
        {
            ListColumn.Width = new GridLength(0);
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            ListPanel.Visibility = Visibility.Collapsed;
            EditorScroll.Visibility = Visibility.Visible;
            BackToListButton.Visibility = Visibility.Visible;
            HomeButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            ListColumn.Width = new GridLength(1, GridUnitType.Star);
            EditorColumn.Width = new GridLength(0);
            ListPanel.Visibility = Visibility.Visible;
            EditorScroll.Visibility = Visibility.Collapsed;
            BackToListButton.Visibility = Visibility.Collapsed;
            HomeButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Move the single toolbar instance between the inline host and the flyout host.</summary>
    private void SetToolbarCollapsed(bool collapsed)
    {
        if (collapsed)
        {
            if (ReferenceEquals(ToolbarHost.Content, EditorToolbar))
                ToolbarHost.Content = null;
            if (!ReferenceEquals(ToolbarFlyoutHost.Content, EditorToolbar))
                ToolbarFlyoutHost.Content = EditorToolbar;
            ToolbarHost.Visibility = Visibility.Collapsed;
            ToolbarMoreButton.Visibility = Visibility.Visible;
        }
        else
        {
            if (ReferenceEquals(ToolbarFlyoutHost.Content, EditorToolbar))
                ToolbarFlyoutHost.Content = null;
            if (!ReferenceEquals(ToolbarHost.Content, EditorToolbar))
                ToolbarHost.Content = EditorToolbar;
            ToolbarHost.Visibility = Visibility.Visible;
            ToolbarMoreButton.Visibility = Visibility.Collapsed;
        }
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        _compactDetail = false;
        ApplyCompactPane();
    }

    // ----- Markdown composer ({} tool): type md, Preview renders it into the note at the caret -----
    private int _mdCaretPos;

    private void InsertCodeBlock_Click(object sender, RoutedEventArgs e)
    {
        _mdCaretPos = EditorBox.Document.Selection?.EndPosition ?? 0;   // remember where to drop it
        MdComposerBox.Text = "";
        MdComposer.Visibility = Visibility.Visible;
        MdComposerBox.Focus(FocusState.Programmatic);
    }

    private void MdCancel_Click(object sender, RoutedEventArgs e)
    {
        MdComposer.Visibility = Visibility.Collapsed;
        MdComposerBox.Text = "";
    }

    private void MdRender_Click(object sender, RoutedEventArgs e)
    {
        var md = MdComposerBox.Text ?? "";
        try
        {
            EditorBox.Document.Selection.SetRange(_mdCaretPos, _mdCaretPos);
            InsertMarkdown(md);
        }
        catch { /* best-effort render */ }

        MdComposer.Visibility = Visibility.Collapsed;
        MdComposerBox.Text = "";
        EditorBox.Focus(FocusState.Programmatic);
        OnContentChanged(this, null!);
    }

    /// <summary>Render Markdown into the RichEditBox at the selection (headings, code, bullets, inline).</summary>
    private void InsertMarkdown(string md)
    {
        var doc = EditorBox.Document;
        var sel = doc.Selection;
        var noteSize = (float)EditorBox.FontSize;
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var inCode = false;
        var code = new StringBuilder();
        var first = true;

        void Span(string text, double size, bool bold, bool italic, bool mono)
        {
            if (text.Length == 0)
                return;
            var p0 = sel.EndPosition;
            sel.TypeText(text);
            var r = doc.GetRange(p0, sel.EndPosition);
            r.CharacterFormat.Size = size > 0 ? (float)size : noteSize;
            r.CharacterFormat.Bold = bold ? Microsoft.UI.Text.FormatEffect.On : Microsoft.UI.Text.FormatEffect.Off;
            r.CharacterFormat.Italic = italic ? Microsoft.UI.Text.FormatEffect.On : Microsoft.UI.Text.FormatEffect.Off;
            r.CharacterFormat.Name = mono ? "Consolas" : "Segoe UI";
            sel.SetRange(sel.EndPosition, sel.EndPosition);
        }

        void Inlines(string text, double size, bool baseBold)
        {
            var pos = 0;
            foreach (Match m in MdInline.Matches(text))
            {
                if (m.Index > pos)
                    Span(text[pos..m.Index], size, baseBold, false, false);
                var tok = m.Value;
                if (tok.StartsWith("**")) Span(tok[2..^2], size, true, false, false);
                else if (tok.StartsWith("`")) Span(tok[1..^1], size, baseBold, false, true);
                else Span(tok[1..^1], size, baseBold, true, false);
                pos = m.Index + m.Length;
            }
            if (pos < text.Length)
                Span(text[pos..], size, baseBold, false, false);
        }

        void NewLine() => sel.TypeText("\r");

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode)
                {
                    if (!first) NewLine();
                    Span(code.ToString().TrimEnd('\n').Replace("\n", "\r"), noteSize, false, false, true);
                    code.Clear();
                    inCode = false;
                    first = false;
                }
                else
                {
                    inCode = true;
                }
                continue;
            }
            if (inCode)
            {
                code.Append(line).Append('\n');
                continue;
            }
            if (!first) NewLine();
            first = false;
            if (line.StartsWith("### ")) Inlines(line[4..], 15, true);
            else if (line.StartsWith("## ")) Inlines(line[3..], 18, true);
            else if (line.StartsWith("# ")) Inlines(line[2..], 22, true);
            else if (line.StartsWith("- ") || line.StartsWith("* ")) Inlines("•  " + line[2..], noteSize, false);
            else Inlines(line, noteSize, false);
        }
        if (inCode && code.Length > 0)
        {
            if (!first) NewLine();
            Span(code.ToString().TrimEnd('\n').Replace("\n", "\r"), noteSize, false, false, true);
        }
    }

    // ----- Markdown blocks ({} tool): monospace md source + a rendered Preview -----
    private void DeleteMdBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MdBlockViewModel vm)
            Vm.RemoveMdBlock(vm);
    }

    private void MdPreview_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MdBlockViewModel vm
            && vm.Preview && FindInTemplate(fe, "MdPreviewBlock") is RichTextBlock rtb)
            RenderMarkdownInto(rtb, vm.Content);
    }

    private void MdPreview_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RichTextBlock rtb && rtb.DataContext is MdBlockViewModel vm && vm.Preview)
            RenderMarkdownInto(rtb, vm.Content);
    }

    private static FrameworkElement? FindInTemplate(FrameworkElement from, string name)
    {
        FrameworkElement? cur = from;
        while (cur is not null)
        {
            if (cur.FindName(name) is FrameworkElement found)
                return found;
            cur = cur.Parent as FrameworkElement;
        }
        return null;
    }

    private static readonly Regex MdInline = new(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*)", RegexOptions.Compiled);

    /// <summary>Minimal Markdown → RichTextBlock: headings, fenced code, bullets, bold/italic/inline-code.</summary>
    private static void RenderMarkdownInto(RichTextBlock rtb, string? md)
    {
        rtb.Blocks.Clear();
        var lines = (md ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var inCode = false;
        var code = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode) { rtb.Blocks.Add(CodeParagraph(code.ToString())); code.Clear(); inCode = false; }
                else inCode = true;
                continue;
            }
            if (inCode) { code.Append(line).Append('\n'); continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;

            var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
            if (line.StartsWith("### ")) AddInlines(p, line[4..], 14, FontWeights.SemiBold);
            else if (line.StartsWith("## ")) AddInlines(p, line[3..], 16, FontWeights.SemiBold);
            else if (line.StartsWith("# ")) AddInlines(p, line[2..], 19, FontWeights.Bold);
            else if (line.StartsWith("- ") || line.StartsWith("* ")) AddInlines(p, "•  " + line[2..]);
            else AddInlines(p, line);
            rtb.Blocks.Add(p);
        }
        if (inCode && code.Length > 0)
            rtb.Blocks.Add(CodeParagraph(code.ToString()));
    }

    private static Paragraph CodeParagraph(string code)
    {
        var p = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
        p.Inlines.Add(new Run { Text = code.TrimEnd('\n'), FontFamily = new FontFamily("Consolas"), FontSize = 13 });
        return p;
    }

    private static void AddInlines(Paragraph p, string text, double baseSize = 0, Windows.UI.Text.FontWeight? baseWeight = null)
    {
        void Add(Run r)
        {
            if (baseSize > 0) r.FontSize = baseSize;
            p.Inlines.Add(r);
        }

        var pos = 0;
        foreach (Match m in MdInline.Matches(text))
        {
            if (m.Index > pos)
                Add(new Run { Text = text[pos..m.Index], FontWeight = baseWeight ?? FontWeights.Normal });
            var tok = m.Value;
            if (tok.StartsWith("**"))
                Add(new Run { Text = tok[2..^2], FontWeight = FontWeights.SemiBold });
            else if (tok.StartsWith("`"))
                Add(new Run { Text = tok[1..^1], FontFamily = new FontFamily("Consolas") });
            else
                Add(new Run { Text = tok[1..^1], FontStyle = Windows.UI.Text.FontStyle.Italic });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            Add(new Run { Text = text[pos..], FontWeight = baseWeight ?? FontWeights.Normal });
    }

    private bool _newOnLoad;
    private long _openNoteId;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter as string == "new")
            _newOnLoad = true;          // create a note once the list has loaded
        else if (e.Parameter is long id)
            _openNoteId = id;           // open this specific note (from Favorites / search)
    }

    private void BuildSwatches()
    {
        SwatchGrid.Items.Clear();
        var dark = ActualTheme == ElementTheme.Dark;
        foreach (var (_, hex) in EditorSwatches.Visible(dark))
        {
            var brush = hex == "auto"
                ? new SolidColorBrush(Color.FromArgb(255, 0x88, 0x88, 0x88))
                : new SolidColorBrush(ColorFromHex(hex));
            SwatchGrid.Items.Add(new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = brush,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0x80, 0x80, 0x80)),
                Tag = hex,
            });
        }
    }

    // ----- note content load (RTF) -----
    private void OnContentRequested(long id)
    {
        _loading = true;
        var rtf = Vm.GetContentRtf(id);
        if (rtf is { Length: > 0 })
        {
            EditorBox.Document.SetText(TextSetOptions.FormatRtf, Encoding.UTF8.GetString(rtf));
        }
        else
        {
            // Minimal empty RTF resets list/heading/char formatting carried over from the previous note.
            EditorBox.Document.SetText(TextSetOptions.FormatRtf, @"{\rtf1\ansi }");
        }
        EditorBox.PlaceholderText = "Start writing…";
        _loading = false;
        LoadAttachments(id);
        ApplyTitleColor();
    }

    /// <summary>Paint the title box with the note's stored title color (none → inherit theme).</summary>
    private void ApplyTitleColor()
    {
        var hex = Vm.CurrentTitleColorHex;
        if (string.IsNullOrEmpty(hex))
            // ClearValue, NOT `Foreground = null`: a local null overrides the theme-aware
            // default, freezing the title at whatever color the current theme renders —
            // after a light/dark switch the title stays black-on-dark / white-on-light.
            TitleBox.ClearValue(Control.ForegroundProperty);
        else
            TitleBox.Foreground = new SolidColorBrush(ColorFromHex(hex));
    }

    // ----- attachments (M7) -----
    public ObservableCollection<AttachmentThumbViewModel> Attachments { get; } = new();

    private void ApplyEditorFontSize()
    {
        var fs = App.GetService<ISettingsService>().GetInt("editor_font_size", 16);
        if (fs is >= 10 and <= 48)
            EditorBox.FontSize = fs;
    }

    private void LoadAttachments(long noteId)
    {
        Attachments.Clear();
        var svc = App.GetService<IAttachmentService>();
        foreach (var a in svc.GetForNote(noteId))
            Attachments.Add(new AttachmentThumbViewModel(a.Id, svc.AbsolutePath(a)));
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.CurrentNoteId == 0)
            return;
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" })
            picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainShell));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;
        var bytes = await File.ReadAllBytesAsync(file.Path);
        SaveAttachment(bytes, Path.GetExtension(file.Path));
    }

    private async void PasteImage_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.CurrentNoteId == 0)
            return;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Bitmap))
            {
                DictationStatus.Text = "No image on the clipboard.";
                return;
            }
            var bmpRef = await content.GetBitmapAsync();
            using var stream = await bmpRef.OpenReadAsync();
            var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            SaveAttachment(bytes, ".png");
        }
        catch
        {
            DictationStatus.Text = "Couldn't paste that image.";
        }
    }

    private void SaveAttachment(byte[] bytes, string ext)
    {
        var svc = App.GetService<IAttachmentService>();
        var a = svc.SaveImage(Vm.CurrentNoteId, bytes, ext);
        Attachments.Add(new AttachmentThumbViewModel(a.Id, svc.AbsolutePath(a)));
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is long id)
        {
            App.GetService<IAttachmentService>().Remove(id);
            var thumb = Attachments.FirstOrDefault(t => t.Id == id);
            if (thumb is not null)
                Attachments.Remove(thumb);
        }
    }

    private void Editor_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Editor_Drop(object sender, DragEventArgs e)
    {
        // Guarded end-to-end: virtual drops (image from a browser, Outlook attachment, file
        // inside a .zip) have StorageFile.Path == "" and would crash this async-void handler.
        try
        {
            if (Vm.CurrentNoteId == 0 || !e.DataView.Contains(StandardDataFormats.StorageItems))
                return;
            foreach (var item in await e.DataView.GetStorageItemsAsync())
            {
                if (item is StorageFile file && IsImageExt(file.FileType))
                {
                    var buffer = await FileIO.ReadBufferAsync(file);   // works for path-less virtual files too
                    SaveAttachment(buffer.ToArray(), file.FileType);
                }
            }
        }
        catch { /* unreadable drop source — ignore */ }
    }

    private static bool IsImageExt(string ext)
        => ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";

    // ----- AI assistant (M8): the global AI ball (MainWindow) owns the agentic plan→apply UI and
    // dictation. It calls back into the open note for summarize / rephrase via these hooks. -----
    public bool HasActiveNote => Vm.CurrentNoteId != 0;
    public long? CurrentNoteIdOrNull => Vm.CurrentNoteId == 0 ? null : Vm.CurrentNoteId;

    /// <summary>Summarize the open note and append the summary at the end.</summary>
    public async Task<string> SummarizeActiveNoteAsync()
    {
        var svc = App.GetService<CacheNote.Core.Ai.AiAssistService>();
        EditorBox.Document.GetText(TextGetOptions.None, out var plain);
        var summary = await svc.SummarizeAsync(plain);
        var end = EditorBox.Document.GetRange(int.MaxValue, int.MaxValue);
        end.SetText(TextSetOptions.None, "\n\nSummary: " + summary + "\n");
        OnContentChanged(this, null!);
        return "Summary inserted at the end of the note.";
    }

    /// <summary>Rephrase the editor's current selection in place.</summary>
    public async Task<string> RephraseSelectionAsync()
    {
        var sel = EditorBox.Document.Selection.Text;
        if (string.IsNullOrWhiteSpace(sel))
            return "Select some text in the note to rephrase.";
        var svc = App.GetService<CacheNote.Core.Ai.AiAssistService>();
        var rephrased = await svc.RephraseAsync(sel);
        EditorBox.Document.Selection.SetText(TextSetOptions.None, rephrased);
        OnContentChanged(this, null!);
        return "Selection rephrased.";
    }

    /// <summary>Refresh the notes list + tag filter after the AI applied actions.</summary>
    public void ReloadAfterAi()
    {
        SaveNow();   // reload re-selects a note — flush pending edits first
        Vm.LoadList();
        RefreshTagFilter();
    }

    /// <summary>Flush the debounced autosave immediately (called on real app exit / update install).</summary>
    public void FlushPendingSave() => SaveNow();

    // ----- list management -----
    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is not NoteListItemViewModel item || item.Id == Vm.CurrentNoteId)
            return;
        SaveNow();
        Vm.Select(item);
        if (_compact)
        {
            _compactDetail = true;   // tapping a note opens it full-width
            ApplyCompactPane();
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => CreateNewNote();

    /// <summary>Create a fresh note and focus its title. Public so the tray / global hotkey can trigger it.</summary>
    public void CreateNewNote()
    {
        SaveNow();
        var id = Vm.NewNote();
        Vm.Select(Vm.Notes.FirstOrDefault(n => n.Id == id));
        if (_compact)
        {
            _compactDetail = true;   // jump straight to the new note's editor
            ApplyCompactPane();
        }
        TitleBox.Focus(FocusState.Programmatic);
    }

    private void ToggleList_Click(object sender, RoutedEventArgs e)
    {
        _listVisible = !_listVisible;
        ListColumn.Width = _listVisible ? new GridLength(288) : new GridLength(0);
        ListPanel.Visibility = _listVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static NoteListItemViewModel? ItemOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as NoteListItemViewModel;

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        SaveNow();
        Vm.Select(item);
        Vm.TogglePinCommand.Execute(null);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        SaveNow();
        Vm.Select(item);
        Vm.ToggleFavoriteCommand.Execute(null);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        SaveNow();
        Vm.Select(item);
        var id = Vm.Duplicate();
        if (id != 0)
            Vm.Select(Vm.Notes.FirstOrDefault(n => n.Id == id));
    }

    private void ConvertToTask_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        var title = string.IsNullOrWhiteSpace(item.Title) ? "Untitled task" : item.Title;
        App.GetService<ITaskService>().ConvertNoteToTask(item.Id, title, item.Snippet);
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        SaveNow();
        Vm.Select(item);
        Vm.ArchiveCurrent();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        SaveNow();
        Vm.Select(item);
        Vm.DeleteCurrent();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    // ----- autosave (debounced) -----
    private void OnContentChanged(object sender, RoutedEventArgs e)
    {
        // Re-measure so the auto-sizing editor shrinks when text is deleted (not just grows).
        EditorBox.InvalidateMeasure();
        if (_loading)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (Vm.CurrentNoteId == 0)
            return;
        EditorBox.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        EditorBox.Document.GetText(TextGetOptions.None, out var plain);
        Vm.SaveContent(Encoding.UTF8.GetBytes(rtf), plain.Trim());
    }

    // ----- placeholder -----
    private void Editor_GotFocus(object sender, RoutedEventArgs e)
    {
        EditorBox.PlaceholderText = "";
        UpdateToolbarState();
    }

    private void Editor_LostFocus(object sender, RoutedEventArgs e)
    {
        EditorBox.Document.GetText(TextGetOptions.None, out var text);
        if (string.IsNullOrWhiteSpace(text))
            EditorBox.PlaceholderText = "Start writing…";
    }

    // ----- toolbar active-state -----
    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateToolbarState();

    private void UpdateToolbarState()
    {
        if (_loading)
            return;

        _syncing = true;
        try
        {
            var cf = Selection.CharacterFormat;
            BoldToggle.IsChecked = cf.Bold == FormatEffect.On;
            ItalicToggle.IsChecked = cf.Italic == FormatEffect.On;
            UnderlineToggle.IsChecked = cf.Underline == UnderlineType.Single;

            var pf = Selection.ParagraphFormat;
            BulletToggle.IsChecked = pf.ListType == MarkerType.Bullet;
            NumberToggle.IsChecked = pf.ListType == MarkerType.Arabic;

            // List types are mutually exclusive (owner request): inside a circle list the
            // bullet/number tools are disabled, and inside a bullet/numbered list the circle
            // tool is disabled — no silent conversion.
            var anyCircled = ParagraphStarts(Selection.StartPosition, Selection.EndPosition).Exists(IsCircled);
            BulletToggle.IsEnabled = !anyCircled;
            NumberToggle.IsEnabled = !anyCircled;
            CircleButton.IsEnabled = anyCircled || pf.ListType == MarkerType.None;   // off inside bullet/numbered lists

            if (!string.IsNullOrEmpty(cf.Name) && Array.IndexOf(Fonts, cf.Name) >= 0)
                FontFamilyCombo.SelectedItem = cf.Name;
            if (cf.Size > 0)
                FontSizeCombo.Text = ((int)cf.Size).ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncing = false;
        }
    }

    // ----- formatting -----
    private ITextSelection Selection => EditorBox.Document.Selection;

    private void AfterFormat()
    {
        EditorBox.Focus(FocusState.Programmatic);
        UpdateToolbarState();
        OnContentChanged(this, null!);
    }

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        Selection.CharacterFormat.Bold = FormatEffect.Toggle;
        AfterFormat();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        Selection.CharacterFormat.Italic = FormatEffect.Toggle;
        AfterFormat();
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        var cf = Selection.CharacterFormat;
        cf.Underline = cf.Underline == UnderlineType.Single ? UnderlineType.None : UnderlineType.Single;
        AfterFormat();
    }

    private void ApplyHeading(float size)
    {
        var cf = Selection.CharacterFormat;
        cf.Size = size;
        cf.Bold = size > 16 ? FormatEffect.On : FormatEffect.Off;
        AfterFormat();
    }

    private void H1_Click(object sender, RoutedEventArgs e) => ApplyHeading(28);
    private void H2_Click(object sender, RoutedEventArgs e) => ApplyHeading(22);
    private void H3_Click(object sender, RoutedEventArgs e) => ApplyHeading(18);
    private void Body_Click(object sender, RoutedEventArgs e) => ApplyHeading(16);

    private void Bullets_Click(object sender, RoutedEventArgs e)
    {
        var turningOn = Selection.ParagraphFormat.ListType != MarkerType.Bullet;
        if (turningOn)
            StripCircleMarkers(ParagraphStarts(Selection.StartPosition, Selection.EndPosition));
        // Re-read the format AFTER the strip — deleting markers shifts the selection.
        Selection.ParagraphFormat.ListType = turningOn ? MarkerType.Bullet : MarkerType.None;
        if (turningOn)
            EnsurePlainLineBelowList();
        AfterFormat();
    }

    private void Numbered_Click(object sender, RoutedEventArgs e)
    {
        var turningOn = Selection.ParagraphFormat.ListType != MarkerType.Arabic;
        if (turningOn)
        {
            StripCircleMarkers(ParagraphStarts(Selection.StartPosition, Selection.EndPosition));
            var pf = Selection.ParagraphFormat;
            pf.ListStart = 1; // numbering starts at 1, not 0
            pf.ListType = MarkerType.Arabic;
            EnsurePlainLineBelowList();
        }
        else
        {
            Selection.ParagraphFormat.ListType = MarkerType.None;
        }
        AfterFormat();
    }

    // ----- circle list: "○" items inline in the note, starting AT the caret -----
    // (The old checklist tool appended checkbox rows in a fixed block BELOW the note text;
    // existing notes still render those, but the toolbar circle now works like the other
    // list tools: it marks the paragraph(s) the caret/selection is on.)
    private const string CircleMarker = "○";       // hollow circle glyph
    private const string CirclePrefix = "○  ";     // marker + two spaces

    /// <summary>Start offset of every paragraph the [selStart, selEnd] selection touches.</summary>
    private List<int> ParagraphStarts(int selStart, int selEnd)
    {
        var doc = EditorBox.Document;
        var starts = new List<int>();
        var probe = doc.GetRange(selStart, selStart);
        probe.StartOf(TextRangeUnit.Paragraph, false);
        while (true)
        {
            if (starts.Count > 0 && probe.StartPosition <= starts[^1])
                break;                                        // no forward progress = end of doc
            starts.Add(probe.StartPosition);
            if (probe.Move(TextRangeUnit.Paragraph, 1) <= 0 || probe.StartPosition > selEnd)
                break;
        }
        return starts;
    }

    private bool IsCircled(int paraStart)
        => (EditorBox.Document.GetRange(paraStart, paraStart + 1).Text ?? "").StartsWith(CircleMarker, StringComparison.Ordinal);

    private int CirclePrefixLen(int paraStart)
    {
        var t = EditorBox.Document.GetRange(paraStart, paraStart + CirclePrefix.Length + 1).Text ?? "";
        var len = 1;
        while (len < t.Length && t[len] == ' ')
            len++;
        return len;
    }

    /// <summary>Strip "○ " markers from the given paragraphs (list types are mutually exclusive).</summary>
    private void StripCircleMarkers(List<int> starts)
    {
        for (var i = starts.Count - 1; i >= 0; i--)           // back-to-front keeps offsets valid
            if (IsCircled(starts[i]))
                EditorBox.Document.GetRange(starts[i], starts[i] + CirclePrefixLen(starts[i])).Text = "";
    }

    /// <summary>
    /// A list block must always have a plain line below it (owner request) — otherwise a
    /// list at the end of the note leaves nowhere to click to continue normal writing.
    /// Appends an empty non-list paragraph when the selection's paragraph is the last one.
    /// </summary>
    private void EnsurePlainLineBelowList()
    {
        var doc = EditorBox.Document;
        var sel = doc.Selection;
        int selStart = sel.StartPosition, selEnd = sel.EndPosition;

        var para = doc.GetRange(selEnd, selEnd);
        para.Expand(TextRangeUnit.Paragraph);
        var docEnd = doc.GetRange(int.MaxValue, int.MaxValue).EndPosition;
        if (para.EndPosition < docEnd)
            return;   // something already exists below the list

        var insert = doc.GetRange(docEnd, docEnd);
        insert.Text = "\r";
        var fresh = doc.GetRange(insert.EndPosition, insert.EndPosition);
        fresh.ParagraphFormat.ListType = MarkerType.None;   // the new line must not inherit the list
        sel.SetRange(selStart, selEnd);                     // keep the caret where the user is typing
    }

    private void CircleList_Click(object sender, RoutedEventArgs e)
    {
        var doc = EditorBox.Document;
        var sel = doc.Selection;
        int selStart = sel.StartPosition, selEnd = sel.EndPosition;
        var starts = ParagraphStarts(selStart, selEnd);

        var removeAll = starts.TrueForAll(IsCircled);
        if (!removeAll)
        {
            // Circle list is exclusive with bullets/numbering — clear those off the lines first.
            var para = doc.GetRange(selStart, selEnd);
            para.Expand(TextRangeUnit.Paragraph);
            para.ParagraphFormat.ListType = MarkerType.None;
        }
        for (var i = starts.Count - 1; i >= 0; i--)           // back-to-front keeps offsets valid
        {
            var s = starts[i];
            if (removeAll)
                doc.GetRange(s, s + CirclePrefixLen(s)).Text = "";
            else if (!IsCircled(s))
                doc.GetRange(s, s).Text = CirclePrefix;
        }

        // Keep the caret on the first touched line, shifted past an inserted marker.
        var caret = removeAll
            ? Math.Max(starts[0], selStart - CirclePrefix.Length)
            : selStart + (IsCircled(starts[0]) && selStart - starts[0] < CirclePrefix.Length ? CirclePrefix.Length : 0);
        sel.SetRange(caret, caret);
        if (!removeAll)
            EnsurePlainLineBelowList();
        AfterFormat();
    }

    /// <summary>Enter inside a "○ " line continues the circle list; Enter on an empty item ends it.</summary>
    private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;
        var doc = EditorBox.Document;
        var sel = doc.Selection;
        if (sel.StartPosition != sel.EndPosition)
            return;                                           // replacing a selection: default behavior

        var line = doc.GetRange(sel.StartPosition, sel.StartPosition);
        line.Expand(TextRangeUnit.Paragraph);
        var text = (line.Text ?? "").TrimEnd('\r', '\n');
        if (!text.StartsWith(CircleMarker, StringComparison.Ordinal))
            return;

        e.Handled = true;
        if (text.Trim() == CircleMarker)
        {
            // Empty item: end the list — strip the marker, leave a plain line.
            var len = 1;
            while (len < text.Length && text[len] == ' ')
                len++;
            doc.GetRange(line.StartPosition, line.StartPosition + len).Text = "";
        }
        else
        {
            sel.TypeText("\r" + CirclePrefix);
            EnsurePlainLineBelowList();   // growing the list keeps a plain line under it
        }
        OnContentChanged(this, null!);
    }

    private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FontFamilyCombo.SelectedItem is not string name)
            return;
        Selection.CharacterFormat.Name = name;
        AfterFormat();
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FontSizeCombo.SelectedItem is not string s)
            return;
        ApplyFontSize(s);
    }

    private void FontSize_Submitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (_syncing)
            return;
        ApplyFontSize(args.Text);
    }

    private void ApplyFontSize(string text)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size is >= 6 and <= 200)
        {
            Selection.CharacterFormat.Size = size;
            AfterFormat();
        }
    }

    // Selection highlight shown while picking (so the user sees what's selected) vs hidden
    // (so the live color shows instead of a blue overlay once they start changing it).
    private readonly SolidColorBrush _selVisibleBrush = new(Color.FromArgb(255, 0x25, 0x63, 0xEB));
    private Color? _pendingColor;
    private Color? _latestColor;

    private bool _colorTargetTitle;   // color tool aimed at the note TITLE (TextBox), not the body

    // Color-flyout event trace for the UI tests (only active when the harness sets
    // CacheNote_DATA_DIR): popup open/close ordering is flaky under automation, and the
    // tests use this log both to verify commits and to detect when the flyout really closed.
    private static void ColorDiag(string m)
    {
        var dir = Environment.GetEnvironmentVariable("CacheNote_DATA_DIR");
        if (string.IsNullOrEmpty(dir))
            return;
        try { File.AppendAllText(Path.Combine(dir, "color-diag.log"), $"{DateTime.Now:HH:mm:ss.fff} {m}\n"); } catch { }
    }

    private void ColorFlyout_Opening(object sender, object e)
    {
        ColorDiag($"OPENING title={TitleBox.FocusState != FocusState.Unfocused}");
        // The tool colors whichever text the user was in: the title box (focused when the
        // flyout opened — the color button doesn't steal focus) or the editor selection.
        _colorTargetTitle = TitleBox.FocusState != FocusState.Unfocused;

        // Capture the range to recolor. Do NOT seed the picker from the current text color —
        // light-mode default text is near-black (#18181B), which drops the picker brightness
        // so picks come out dark.
        var sel = EditorBox.Document.Selection;
        _selStart = sel.StartPosition;
        _selEnd = sel.EndPosition;
        _pendingColor = null;
        _latestColor = null;

        // Keep the selection visible (blue) while picking so the user sees what they're recoloring.
        EditorBox.SelectionHighlightColor = _selVisibleBrush;
        EditorBox.SelectionHighlightColorWhenNotFocused = _selVisibleBrush;
    }

    // Live recolor: applies to the captured range without stealing focus (so the picker
    // stays interactive and the drag is smooth), and drops the blue highlight so the real
    // color is visible immediately.
    private void LiveApply(Color color)
    {
        // Title target: the title TextBox is single-color — recolor the whole thing live.
        if (_colorTargetTitle)
        {
            TitleBox.Foreground = new SolidColorBrush(color);
            ColorBar.Fill = new SolidColorBrush(color);
            _pendingColor = color;
            return;
        }

        // Editor keeps focus (AllowFocusOnInteraction=False on the button + picker), so applying
        // via the live Selection both lands AND repaints immediately. Hide the selection
        // highlight so the actual color shows instead of a blue overlay.
        // Make the selection highlight match the editor background so it's invisible — the
        // colored text shows through. (A transparent highlight renders BLACK in RichEditBox.)
        if (EditorBox.Background is SolidColorBrush bg)
        {
            EditorBox.SelectionHighlightColor = bg;
            EditorBox.SelectionHighlightColorWhenNotFocused = bg;
        }
        var sel = EditorBox.Document.Selection;
        sel.SetRange(_selStart, _selEnd);
        sel.CharacterFormat.ForegroundColor = color;
        RecolorListMarkers(color);
        ColorBar.Fill = new SolidColorBrush(color);
        _pendingColor = color;
    }

    // Bullet/number markers take the colour of their list paragraph (not the selected sub-range),
    // so when the selection is inside a list, recolor the whole list paragraph(s) it touches —
    // that way the marker changes colour with the text.
    private void RecolorListMarkers(Color color)
    {
        var doc = EditorBox.Document;
        var sel = doc.Selection;
        if (sel.ParagraphFormat.ListType == MarkerType.None)
            return;
        var para = doc.GetRange(sel.StartPosition, sel.EndPosition);
        para.Expand(Microsoft.UI.Text.TextRangeUnit.Paragraph);
        para.CharacterFormat.ForegroundColor = color;
    }

    private void CustomColor_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncing)
            return;
        // Do NOT touch the RichEdit document while the picker holds pointer capture — any
        // mutation mid-drag leaves the editor's text surface blank until the next pointer
        // event (ApplyDisplayUpdates can't flush it; an unbalanced one even freezes painting
        // for good). Track the color live on the toolbar swatch bar; the text itself is
        // recolored on release/close. The title is a plain TextBox, so IT can recolor live.
        _latestColor = args.NewColor;
        ColorDiag($"COLORCHANGED {args.NewColor} title={_colorTargetTitle}");
        ColorBar.Fill = new SolidColorBrush(args.NewColor);
        if (_colorTargetTitle)
        {
            TitleBox.Foreground = new SolidColorBrush(args.NewColor);
            _pendingColor = args.NewColor;
        }
    }

    private void Color_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Border border && border.Tag is string hex)
        {
            var color = hex == "auto" ? ThemedTextColor() : ColorFromHex(hex);
            LiveApply(color);
            _latestColor = color;   // the swatch wins over any earlier spectrum drag at commit time
            (ColorButton.Flyout as Flyout)?.Hide();   // swatch = instant pick → close + finalize
        }
    }

    // ----- attach a reminder to the current note (M3) -----
    private void RemindFlyout_Opening(object sender, object e)
    {
        // Seed the pickers to one hour out so they aren't empty, and hint the note title.
        var when = DateTime.Now.AddHours(1);
        RemindDate.Date = new DateTimeOffset(when.Date);
        RemindTime.Time = when.TimeOfDay;
        RemindMessage.PlaceholderText = string.IsNullOrWhiteSpace(Vm.Title) ? "Message" : Vm.Title;
    }

    private void SetReminder_Click(object sender, RoutedEventArgs e)
    {
        var date = RemindDate.Date?.DateTime ?? DateTime.Now;
        var when = date.Date + RemindTime.Time;
        if (when <= DateTime.Now)
            when = DateTime.Now.AddMinutes(5);   // never schedule in the past
        var repeat = ((RemindRepeat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Once").ToLowerInvariant();
        var msg = string.IsNullOrWhiteSpace(RemindMessage.Text) ? Vm.Title : RemindMessage.Text;
        long? noteId = Vm.CurrentNoteId == 0 ? null : Vm.CurrentNoteId;
        App.GetService<IReminderService>().Create(noteId, msg, when.ToUniversalTime(), repeat);
        RemindButton.Flyout?.Hide();
    }

    // ----- search + tags (M4) -----
    private void RefreshTagFilter()
    {
        _syncing = true;
        TagFilter.Items.Clear();
        TagFilter.Items.Add(new ComboBoxItem { Content = "All notes", Tag = 0L });
        foreach (var t in Vm.AllTags)
            TagFilter.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Id });
        TagFilter.SelectedIndex = 0;
        _syncing = false;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        // Filtering re-selects a note and reloads the editor; flush pending edits first or a
        // typing burst (never older than the 600ms debounce) is silently discarded.
        SaveNow();

        var q = sender.Text?.Trim() ?? "";
        if (q.Length == 0)
        {
            Vm.LoadList();
            return;
        }
        // Searching overrides any active tag filter.
        _syncing = true;
        TagFilter.SelectedIndex = 0;
        _syncing = false;
        Vm.ApplyFilter(q, null);
    }

    private void TagFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
            return;
        if (TagFilter.SelectedItem is ComboBoxItem item && item.Tag is long id)
        {
            SaveNow();   // same as search: the filter switches notes — don't drop pending edits
            if (id == 0)
            {
                Vm.ApplyFilter(null, null);
            }
            else
            {
                SearchBox.Text = "";
                Vm.ApplyFilter(null, id);
            }
        }
    }

    private void FindAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Programmatic);
    }

    // ----- dictation / speech-to-text (M5) -----
    private ISpeechToTextService? _stt;
    private MicCaptureService? _mic;

    private async void Mic_Click(object sender, RoutedEventArgs e)
    {
        if (MicButton.IsChecked == true)
            await StartDictationAsync();
        else
            await StopDictationAsync();
    }

    private async Task StartDictationAsync()
    {
        if (_stt is not null)
            return;   // already listening — ignore a duplicate start

        var factory = App.GetService<ISpeechToTextFactory>();
        var stt = factory.Create();
        if (!stt.IsConfigured)
        {
            DictationStatus.Text = $"Add an API key for '{factory.Provider}' in .env to dictate.";
            MicButton.IsChecked = false;
            return;
        }

        _stt = stt;
        // Only one dictation session at a time (editor mic vs. AI ball) — stop the other first.
        await DictationCoordinator.ClaimAsync(this, StopDictationAsync);

        stt.PartialReceived += OnSttPartial;
        stt.FinalReceived += OnSttFinal;
        stt.ErrorOccurred += OnSttError;

        await stt.StartAsync(CancellationToken.None);

        // Toggled off (or another session claimed the mic) during startup → stop THIS session.
        // The field-based stop would tear down whatever NEW session _stt now points to and
        // leave this one silently streaming.
        if (MicButton.IsChecked != true || !ReferenceEquals(_stt, stt))
        {
            stt.PartialReceived -= OnSttPartial;
            stt.FinalReceived -= OnSttFinal;
            stt.ErrorOccurred -= OnSttError;
            try { await stt.StopAsync(); } catch { /* best effort */ }
            if (ReferenceEquals(_stt, stt))
                await StopDictationAsync();
            return;
        }

        DictationStatus.Text = "Listening…";
        if (stt.NeedsMicrophone)
        {
            _mic = new MicCaptureService();
            _mic.FrameReady += OnMicFrame;
            if (!_mic.Start(msg => DispatcherQueue.TryEnqueue(() => DictationStatus.Text = msg)))
                await StopDictationAsync();
        }
    }

    private async void OnMicFrame(byte[] frame)
    {
        if (_stt is not null)
            try { await _stt.SendAsync(frame); } catch { /* dropped frame */ }
    }

    private void OnSttPartial(string text)
        => DispatcherQueue.TryEnqueue(() => DictationStatus.Text = "… " + text);

    private void OnSttFinal(string text)
        => DispatcherQueue.TryEnqueue(() =>
        {
            InsertDictation(text);
            DictationStatus.Text = "Listening…";
        });

    private void OnSttError(string message)
        => DispatcherQueue.TryEnqueue(() => DictationStatus.Text = message);

    private void InsertDictation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        EditorBox.Focus(FocusState.Programmatic);
        var sel = EditorBox.Document.Selection;
        sel.TypeText(text.EndsWith(' ') ? text : text + " ");
        OnContentChanged(this, null!);
    }

    private async Task StopDictationAsync()
    {
        if (_mic is not null)
        {
            _mic.FrameReady -= OnMicFrame;
            _mic.Dispose();
            _mic = null;
        }
        if (_stt is not null)
        {
            _stt.PartialReceived -= OnSttPartial;
            _stt.FinalReceived -= OnSttFinal;
            _stt.ErrorOccurred -= OnSttError;
            // Guarded like the MainWindow copy: a WS teardown failure inside async-void
            // Mic_Click would otherwise crash the process.
            try { await _stt.StopAsync(); } catch { /* best effort */ }
            _stt = null;
        }
        DictationCoordinator.Release(this);
        DictationStatus.Text = "";
        if (MicButton.IsChecked == true)
            MicButton.IsChecked = false;
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is long id)
            Vm.RemoveTagFromCurrent(id);
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NewTagBox.Text))
        {
            Vm.AddTagToCurrent(NewTagBox.Text);
            NewTagBox.Text = "";
            RefreshTagFilter();
        }
        AddTagButton.Flyout?.Hide();
    }

    private void ColorFlyout_Closed(object sender, object e)
    {
        ColorDiag($"CLOSED title={_colorTargetTitle} latest={_latestColor?.ToString() ?? "null"} pending={_pendingColor?.ToString() ?? "null"} sel={_selStart}..{_selEnd}");
        // Title target: persist the color per note + reflect in the list. Pure black/white
        // is stored as "theme default" so it flips correctly on light/dark switch; any other
        // color sticks across themes.
        if (_colorTargetTitle)
        {
            if ((_latestColor ?? _pendingColor) is Color picked)
            {
                var themed = ThemedTextColor();
                var isDefaultish =
                    (picked.R == 0x00 && picked.G == 0x00 && picked.B == 0x00) ||
                    (picked.R == 0xFF && picked.G == 0xFF && picked.B == 0xFF) ||
                    (picked.R == themed.R && picked.G == themed.G && picked.B == themed.B);   // "auto" swatch
                var hex = isDefaultish ? null : $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
                Vm.SetTitleColorForCurrent(hex);
                ApplyTitleColor();
            }
            _colorTargetTitle = false;
            _latestColor = null;
            _pendingColor = null;
            return;
        }

        EditorBox.SelectionHighlightColor = _selVisibleBrush;
        EditorBox.SelectionHighlightColorWhenNotFocused = _selVisibleBrush;
        EditorBox.Focus(FocusState.Programmatic);

        // Commit AFTER the popup finishes tearing down: CharacterFormat writes issued while
        // the flyout is mid-light-dismiss are silently dropped by RichEdit, so an immediate
        // commit loses the picked color. One dispatcher hop later they land reliably.
        var caretColor = _latestColor ?? _pendingColor;
        int start = _selStart, end = _selEnd;
        DispatcherQueue.TryEnqueue(() =>
        {
            var sel = EditorBox.Document.Selection;
            if (_latestColor is Color last)
            {
                sel.SetRange(start, end);
                sel.CharacterFormat.ForegroundColor = last;
                RecolorListMarkers(last);
                _pendingColor = last;
            }
            // Collapse to the end so the freshly-colored text is VISIBLE (not hidden under
            // the selection highlight), and so a set-then-type caret keeps the picked color.
            sel.SetRange(end, end);
            if (caretColor is Color c)
                sel.CharacterFormat.ForegroundColor = c;
            var probe = EditorBox.Document.GetRange(start, Math.Max(start + 1, Math.Min(end, start + 1)));
            ColorDiag($"COMMITTED picked={caretColor?.ToString() ?? "null"} readback={probe.CharacterFormat.ForegroundColor}");
            OnContentChanged(this, null!);
        });
    }

    // ----- theme swap: keep custom font colors across light/dark -----
    // RichEditBox re-applies its themed Foreground to the WHOLE document when the theme
    // changes, wiping every custom run color. Snapshot the RTF before the swap, restore it
    // after, then flip ONLY theme-default / pure-black / pure-white runs to the new theme's
    // text color — any other color the user picked survives the switch.
    private string? _themeSwapRtf;
    private Color _themeSwapOldDefault;

    public void PrepareThemeSwap()
    {
        _themeSwapRtf = null;
        if (Vm.CurrentNoteId == 0)
            return;
        EditorBox.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        _themeSwapRtf = rtf;
        _themeSwapOldDefault = ThemedTextColor();
    }

    public void FinishThemeSwap()
    {
        if (_themeSwapRtf is not string rtf)
            return;
        _themeSwapRtf = null;
        var oldDefault = _themeSwapOldDefault;
        // Enqueue so the new theme's brushes have propagated before we read ThemedTextColor().
        DispatcherQueue.TryEnqueue(() =>
        {
            _loading = true;
            EditorBox.Document.SetText(TextSetOptions.FormatRtf, rtf);
            RecolorThemeDefaultRuns(oldDefault, ThemedTextColor());
            _loading = false;
            OnContentChanged(this, null!);
        });
    }

    private void RecolorThemeDefaultRuns(Color oldDefault, Color newDefault)
    {
        var doc = EditorBox.Document;
        doc.GetText(TextGetOptions.None, out var text);
        var len = text.Length;
        var probe = doc.GetRange(0, 0);
        var pos = 0;
        while (pos < len)
        {
            probe.SetRange(pos, pos + 1);
            var c = probe.CharacterFormat.ForegroundColor;
            var runEnd = pos + 1;
            while (runEnd < len)
            {
                probe.SetRange(runEnd, runEnd + 1);
                if (probe.CharacterFormat.ForegroundColor != c)
                    break;
                runEnd++;
            }
            if (IsThemeDefault(c, oldDefault))
            {
                var run = doc.GetRange(pos, runEnd);
                run.CharacterFormat.ForegroundColor = newDefault;
            }
            pos = runEnd;
        }
    }

    private static bool IsThemeDefault(Color c, Color oldDefault)
        => (c.R == oldDefault.R && c.G == oldDefault.G && c.B == oldDefault.B)
           || (c.R == 0x00 && c.G == 0x00 && c.B == 0x00)     // pure black follows the theme
           || (c.R == 0xFF && c.G == 0xFF && c.B == 0xFF);    // pure white follows the theme

    /// <summary>
    /// The TextControl* brushes in EditorBox.Resources resolve their {ThemeResource} colors
    /// once at load; an in-session light/dark switch leaves them stale (white editor surface
    /// in dark mode). Re-paint them from the active theme's tokens (ThemeResources.xaml).
    /// </summary>
    private void RefreshEditorThemeBrushes()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var surface = dark ? Color.FromArgb(255, 0x1E, 0x1E, 0x1E) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
        var text = dark ? Color.FromArgb(255, 0xF5, 0xF5, 0xF5) : Color.FromArgb(255, 0x18, 0x18, 0x1B);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            if (EditorBox.Resources[key] is SolidColorBrush b)
                b.Color = surface;
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            if (EditorBox.Resources[key] is SolidColorBrush b)
                b.Color = text;
    }

    // ----- helpers -----
    private Color ThemedTextColor() =>
        EditorBox.Foreground is SolidColorBrush b ? b.Color : Color.FromArgb(255, 0xF5, 0xF5, 0xF5);

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
        return Color.FromArgb(255, r, g, b);
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChecklistItemViewModel item)
            Vm.RemoveItem(item);
    }

    // Custom rounded hover for list rows (children: [0]=selection, [1]=hover, [2]=content).
    private void Item_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g && g.Children.Count > 1 && g.Children[1] is Border hover)
            hover.Opacity = 1;
    }

    private void Item_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g && g.Children.Count > 1 && g.Children[1] is Border hover)
            hover.Opacity = 0;
    }
}
