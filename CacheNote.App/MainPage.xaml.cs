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
using CacheNote.Core.ViewModels;
using CacheNote_App.Controls;
using CacheNote_App.Services;
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

    private static readonly (string Label, string Hex)[] Swatches =
    [
        ("Default", "auto"), ("#18181B", "#18181B"), ("#71717A", "#71717A"),
        ("#2563EB", "#2563EB"), ("#0EA5E9", "#0EA5E9"), ("#16A34A", "#16A34A"),
        ("#D97706", "#D97706"), ("#DC2626", "#DC2626"), ("#7C3AED", "#7C3AED"),
        ("#FFFFFF", "#FFFFFF"),
    ];

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
        ActualThemeChanged += (_, _) => BuildSwatches();
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
        foreach (var (_, hex) in Swatches)
        {
            if (dark && hex == "#18181B") continue;   // black is invisible on dark
            if (!dark && hex == "#FFFFFF") continue;   // white is invisible on light
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
        if (Vm.CurrentNoteId == 0 || !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        foreach (var item in await e.DataView.GetStorageItemsAsync())
        {
            if (item is StorageFile file && IsImageExt(file.FileType))
            {
                var bytes = await File.ReadAllBytesAsync(file.Path);
                SaveAttachment(bytes, file.FileType);
            }
        }
    }

    private static bool IsImageExt(string ext)
        => ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";

    // ----- AI assistant (M8): summarize / rephrase / agentic preview-then-apply -----
    private async void Ai_Click(object sender, RoutedEventArgs e)
    {
        var svc = App.GetService<CacheNote.Core.Ai.AiAssistService>();

        var instruction = new TextBox
        {
            PlaceholderText = "Tell the AI what to do… e.g. \"plan my trip to Goa\"",
            AcceptsReturn = true,
            MinHeight = 64,
            TextWrapping = TextWrapping.Wrap,
        };
        instruction.SetValue(AutomationProperties.AutomationIdProperty, "AiInstruction");

        var status = new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppTextSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        status.SetValue(AutomationProperties.AutomationIdProperty, "AiStatus");

        var actionsPanel = new StackPanel { Spacing = 2 };

        var planButton = new Button { Content = "Plan actions" };
        planButton.SetValue(AutomationProperties.AutomationIdProperty, "AiPlan");
        var summarizeButton = new Button { Content = "Summarize note" };
        var rephraseButton = new Button { Content = "Rephrase selection" };

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonRow.Children.Add(planButton);
        buttonRow.Children.Add(summarizeButton);
        buttonRow.Children.Add(rephraseButton);

        var panel = new StackPanel { Width = 380, Spacing = 10 };
        panel.Children.Add(instruction);
        panel.Children.Add(buttonRow);
        panel.Children.Add(status);
        panel.Children.Add(actionsPanel);

        var dialog = new ContentDialog
        {
            Title = "AI assistant",
            Content = panel,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };

        if (!svc.IsConfigured)
        {
            status.Text = $"AI provider '{svc.Provider}' has no key. Add VERTEX_AI_API_KEY or GEMINI_API_KEY to .env (or set AI_PROVIDER=fake).";
            planButton.IsEnabled = summarizeButton.IsEnabled = rephraseButton.IsEnabled = false;
        }

        List<CacheNote.Core.Ai.AiAction> planned = new();

        planButton.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(instruction.Text)) { status.Text = "Type an instruction first."; return; }
            status.Text = "Thinking…";
            actionsPanel.Children.Clear();
            try
            {
                planned = await svc.PlanAsync(instruction.Text.Trim());
                if (planned.Count == 0) { status.Text = "No actions proposed."; return; }
                foreach (var a in planned)
                    actionsPanel.Children.Add(new TextBlock { Text = "• " + a.Describe(), FontSize = 13, TextWrapping = TextWrapping.Wrap });
                status.Text = $"Planned {planned.Count} action(s). Review, then Apply.";
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (Exception ex) { status.Text = "AI error: " + ex.Message; }
        };

        summarizeButton.Click += async (_, _) =>
        {
            status.Text = "Summarizing…";
            try
            {
                EditorBox.Document.GetText(TextGetOptions.None, out var plain);
                var summary = await svc.SummarizeAsync(plain);
                var end = EditorBox.Document.GetRange(int.MaxValue, int.MaxValue);
                end.SetText(TextSetOptions.None, "\n\nSummary: " + summary + "\n");
                OnContentChanged(this, null!);
                status.Text = "Summary inserted at the end of the note.";
            }
            catch (Exception ex) { status.Text = "AI error: " + ex.Message; }
        };

        rephraseButton.Click += async (_, _) =>
        {
            var sel = EditorBox.Document.Selection.Text;
            if (string.IsNullOrWhiteSpace(sel)) { status.Text = "Select some text to rephrase."; return; }
            status.Text = "Rephrasing…";
            try
            {
                var rephrased = await svc.RephraseAsync(sel);
                EditorBox.Document.Selection.SetText(TextSetOptions.None, rephrased);
                OnContentChanged(this, null!);
                status.Text = "Selection rephrased.";
            }
            catch (Exception ex) { status.Text = "AI error: " + ex.Message; }
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (planned.Count == 0) return;
            var summary = svc.Apply(planned, Vm.CurrentNoteId == 0 ? null : Vm.CurrentNoteId);
            status.Text = summary;
            dialog.IsPrimaryButtonEnabled = false;
            args.Cancel = true;   // keep the dialog open so the result is visible
            Vm.LoadList();
            RefreshTagFilter();
        };

        await dialog.ShowAsync();
    }

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
        var pf = Selection.ParagraphFormat;
        pf.ListType = pf.ListType == MarkerType.Bullet ? MarkerType.None : MarkerType.Bullet;
        AfterFormat();
    }

    private void Numbered_Click(object sender, RoutedEventArgs e)
    {
        var pf = Selection.ParagraphFormat;
        if (pf.ListType == MarkerType.Arabic)
        {
            pf.ListType = MarkerType.None;
        }
        else
        {
            pf.ListStart = 1; // numbering starts at 1, not 0
            pf.ListType = MarkerType.Arabic;
        }
        AfterFormat();
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
    private TextBlock? _colorOverlay;   // live-recolor copy drawn over the selection while picking

    private void ColorFlyout_Opening(object sender, object e)
    {
        // Capture the range to recolor. Do NOT seed the picker from the current text color —
        // light-mode default text is near-black (#18181B), which drops the picker brightness
        // so picks come out dark.
        var sel = EditorBox.Document.Selection;
        _selStart = sel.StartPosition;
        _selEnd = sel.EndPosition;
        _pendingColor = null;
        _latestColor = null;

        // PARKED: live-during-drag overlay (BuildColorOverlay) is disabled for now — alignment
        // unfinished. Color works via swatches (instant) + apply-on-release. Keep the selection
        // visible (blue) while picking so the user sees what they're recoloring.
        EditorBox.SelectionHighlightColor = _selVisibleBrush;
        EditorBox.SelectionHighlightColorWhenNotFocused = _selVisibleBrush;
    }

    // Draw a TextBlock copy of the selected text exactly over the selection. Composition renders
    // it every frame, so recoloring it on ColorChanged updates LIVE during the drag — which the
    // RichEdit text engine refuses to do while the picker holds the pointer. Single line only:
    // a multi-line selection's GetRect is one bounding box (line breaks unknown) → skip overlay,
    // the color still lands on release.
    private void BuildColorOverlay()
    {
        ColorOverlay.Children.Clear();
        _colorOverlay = null;

        var sel = EditorBox.Document.Selection;
        var text = sel.Text ?? string.Empty;
        Diag($"BUILD text='{text.Replace("\r", "\\r")}' len={text.Length}");
        if (string.IsNullOrEmpty(text) || text.Contains('\r') || text.Contains('\n'))
        {
            Diag("SKIP empty-or-multiline");
            return; // nothing selected, or multi-line → no overlay
        }

        sel.GetRect(PointOptions.ClientCoordinates, out Windows.Foundation.Rect r, out _);
        Diag($"RAW rect X={r.X} Y={r.Y} W={r.Width} H={r.Height}");
        if (r.Width <= 0 || r.Height <= 0)
        {
            Diag("SKIP zero-rect");
            return;
        }

        // GetRect is in device pixels; the Canvas is in DIPs → divide by the display scale.
        double scale = EditorBox.XamlRoot?.RasterizationScale ?? 1.0;
        double rx = r.X / scale, ry = r.Y / scale, rw = r.Width / scale, rh = r.Height / scale;
        Diag($"SCALE={scale} -> x={rx:F1} y={ry:F1} w={rw:F1} h={rh:F1}");

        var fmt = sel.CharacterFormat;
        // Derive font size from the measured line height — unit-agnostic, so it matches the real
        // glyphs regardless of point/DIP conversions. Segoe UI line height ≈ 1.33 × font size.
        double size = rh > 0 ? rh / 1.33 : 16;
        var family = string.IsNullOrEmpty(fmt.Name)
            ? (FontFamilyCombo.SelectedItem as string ?? "Segoe UI")
            : fmt.Name;

        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily(family),
            Foreground = new SolidColorBrush(ThemedTextColor()),
            TextWrapping = TextWrapping.NoWrap,
            TextLineBounds = TextLineBounds.Full,
        };
        if (fmt.Bold == FormatEffect.On) tb.FontWeight = FontWeights.Bold;
        if (fmt.Italic == FormatEffect.On) tb.FontStyle = Windows.UI.Text.FontStyle.Italic;

        // TextBlock has no Background in WinUI; a Border supplies the opaque surface fill that
        // hides the original (old-color) text underneath.
        var cover = new Border
        {
            Background = EditorBox.Background,
            Width = rw,
            Height = rh,
            Child = tb,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(cover, rx);
        Canvas.SetTop(cover, ry);
        ColorOverlay.Children.Add(cover);
        _colorOverlay = tb;
        Diag($"ADDED overlay size={size:F1} family={family} canvasChildren={ColorOverlay.Children.Count} canvasW={ColorOverlay.ActualWidth} canvasH={ColorOverlay.ActualHeight}");
    }

    // Color-overlay diagnostics — disabled (parked feature; no hardcoded path so the repo folder is movable).
    private static void Diag(string m, bool reset = false) { }

    // Live recolor: applies to the captured range without stealing focus (so the picker
    // stays interactive and the drag is smooth), and drops the blue highlight so the real
    // color is visible immediately.
    private void LiveApply(Color color)
    {
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
        ColorBar.Fill = new SolidColorBrush(color);
        _pendingColor = color;
    }

    private void CustomColor_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncing)
            return;
        _latestColor = args.NewColor;
        ColorBar.Fill = new SolidColorBrush(args.NewColor);   // bar tracks instantly
        if (_colorOverlay is not null)                        // overlay recolors LIVE (composition)
            _colorOverlay.Foreground = new SolidColorBrush(args.NewColor);
        Diag($"CHG {args.NewColor} overlayNull={_colorOverlay is null}");
    }

    private void Color_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Border border && border.Tag is string hex)
        {
            LiveApply(hex == "auto" ? ThemedTextColor() : ColorFromHex(hex));
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
        var factory = App.GetService<ISpeechToTextFactory>();
        _stt = factory.Create();
        if (!_stt.IsConfigured)
        {
            DictationStatus.Text = $"Add an API key for '{factory.Provider}' in .env to dictate.";
            MicButton.IsChecked = false;
            _stt = null;
            return;
        }

        _stt.PartialReceived += OnSttPartial;
        _stt.FinalReceived += OnSttFinal;
        _stt.ErrorOccurred += OnSttError;

        await _stt.StartAsync(CancellationToken.None);
        DictationStatus.Text = "Listening…";

        if (_stt.NeedsMicrophone)
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
            await _stt.StopAsync();
            _stt = null;
        }
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
        // Commit the picked color to the REAL text range once (the overlay was just a preview),
        // tear down the overlay, restore the normal highlight, and persist.
        if (_latestColor is Color last)
        {
            var range = EditorBox.Document.Selection;
            range.SetRange(_selStart, _selEnd);
            range.CharacterFormat.ForegroundColor = last;
            _pendingColor = last;
        }
        ColorOverlay.Children.Clear();
        _colorOverlay = null;

        EditorBox.SelectionHighlightColor = _selVisibleBrush;
        EditorBox.SelectionHighlightColorWhenNotFocused = _selVisibleBrush;
        EditorBox.Focus(FocusState.Programmatic);
        var sel = EditorBox.Document.Selection;
        // Collapse to the end so the freshly-colored text is VISIBLE (not hidden under the
        // selection highlight), and so a set-then-type caret keeps the picked color.
        sel.SetRange(_selEnd, _selEnd);
        if (_pendingColor is Color c)
            sel.CharacterFormat.ForegroundColor = c;
        OnContentChanged(this, null!);
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
