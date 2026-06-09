using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using CacheNote.Core.Ai;
using CacheNote.Core.Services;
using CacheNote.Core.Speech;
using CacheNote_App.Services;
using CacheNote_App.Interop;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CacheNote_App;

/// <summary>
/// The application window: custom title bar (Mica), a theme toggle in the title-bar
/// footer, and the content Frame. Window-state persistence lands in M1b.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string ThemeKey = "theme";
    private const string SkippedUpdateKey = "update_skipped_version";

    // Segoe Fluent glyphs (built from code points so the source stays plain ASCII).
    private static readonly string SunGlyph = ((char)0xE706).ToString();   // shown in dark mode → click for light
    private static readonly string MoonGlyph = ((char)0xE708).ToString();  // shown in light mode → click for dark

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Restore saved window size/position (clamped to a visible display); else default + center.
        if (!RestoreWindowState())
        {
            AppWindow.Resize(new SizeInt32(1100, 720));
            CenterOnScreen();
        }
        Closed += (_, _) => SaveWindowState();

        // Hard minimum size: below this the responsive layouts (collapsed toolbars,
        // single-pane notes) still render without overlap. Scaled by the display's
        // rasterization scale so the floor is honored at 150%/200% DPI too.
        RootGrid.Loaded += (_, _) => ApplyMinWindowSize();
        // Keep the AI chat panel inside the window at every width/height (it must not clip the
        // left edge at the 380px minimum, nor overflow the bottom at the 520px minimum).
        RootGrid.SizeChanged += (_, e) => AdjustAiPanel(e.NewSize.Width, e.NewSize.Height);
        // Cloud build/publish (GitHub Actions) + auto-check on launch → offer one-click update.
        RootGrid.Loaded += async (_, _) => await StartupUpdateCheckAsync();

        RootFrame.Navigate(typeof(HomePage));

        // When the AI panel finishes closing, fully collapse it (so its controls aren't hit-testable).
        ((Storyboard)RootGrid.Resources["AiCloseStoryboard"]).Completed += (_, _) =>
        {
            if (!_aiOpen)
                AiPanel.Visibility = Visibility.Collapsed;
        };

        // Restore the saved theme (defaults to following the system).
        var saved = App.GetService<ISettingsService>().Get(ThemeKey, nameof(ElementTheme.Default));
        ApplyTheme(Enum.TryParse<ElementTheme>(saved, out var t) ? t : ElementTheme.Default, persist: false);

        SetupTrayAndWindowBehavior();
    }

    private bool _exiting;
    private bool _pinSync;   // guards the title-bar always-on-top toggle from feedback loops
    private GlobalHotkey? _hotkey;

    // Under UI automation, don't persist/restore window geometry: a resize test would otherwise
    // leak a tiny window into the next test (compact mode hides wide-layout controls → cascade).
    private static readonly bool TestMode =
        Environment.GetEnvironmentVariable("CacheNote_NO_SINGLE_INSTANCE") == "1";

    /// <summary>Enforce a minimum window size (in DIPs, converted to physical px for the presenter).</summary>
    private void ApplyMinWindowSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            return;
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        presenter.PreferredMinimumWidth = (int)(380 * scale);
        presenter.PreferredMinimumHeight = (int)(520 * scale);
    }

    private void SetupTrayAndWindowBehavior()
    {
        var settings = App.GetService<ISettingsService>();

        // Hide-to-tray: intercept close and hide instead, unless we're really exiting.
        AppWindow.Closing += OnClosing;

        // Tray icon: left-click opens; the context menu has the rest.
        TrayIcon.ForceCreate();
        TrayIcon.LeftClickCommand = new RelayCommand(ShowAndActivate);

        // Always-on-top (persisted) + reflect both toggle states in the menu.
        var alwaysOnTop = settings.GetBool("always_on_top");
        ApplyAlwaysOnTop(alwaysOnTop, persist: false);
        AlwaysOnTopItem.IsChecked = alwaysOnTop;
        _pinSync = true; PinToggle.IsChecked = alwaysOnTop; _pinSync = false;
        PauseNotifyItem.IsChecked = settings.GetBool("pause_notifications");

        // Global Ctrl+Shift+N → new note, even when the app is in the tray.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hotkey = new GlobalHotkey(hwnd, 0x4E /* VK_N */, () => DispatcherQueue.TryEnqueue(NewNote));

        StartReminderEngine();
    }

    // ----- reminder engine (runs on the UI dispatcher; keeps ticking while in the tray) -----
    private DispatcherQueueTimer? _reminderTimer;

    private void StartReminderEngine()
    {
        PollReminders();   // catch up on anything due while the app was closed

        _reminderTimer = DispatcherQueue.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(20);
        _reminderTimer.IsRepeating = true;
        _reminderTimer.Tick += (_, _) => PollReminders();
        _reminderTimer.Start();
    }

    private void PollReminders()
    {
        // Paused → don't fire (and don't advance, so they fire once unpaused).
        if (App.GetService<ISettingsService>().GetBool("pause_notifications"))
            return;

        var due = App.GetService<IReminderService>().GetDueAndAdvance(DateTime.UtcNow);
        if (due.Count == 0)
            return;

        var toast = App.GetService<ToastService>();
        foreach (var r in due)
            toast.ShowReminder(r.Id, r.Message);

        RefreshRemindersIfOpen();
    }

    /// <summary>Reload the Reminders list if that page is currently shown.</summary>
    public void RefreshRemindersIfOpen()
    {
        if (RootFrame.Content is RemindersPage page)
            page.Vm.Load();
    }

    /// <summary>Navigate to the Reminders section (from a toast "Open").</summary>
    public void NavigateToReminders()
    {
        if (RootFrame.Content is not RemindersPage)
            RootFrame.Navigate(typeof(RemindersPage));
    }

    private void NewNoteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewNote();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        SaveWindowState();
        if (_exiting)
            return;
        e.Cancel = true;   // keep running in the tray
        AppWindow.Hide();
    }

    /// <summary>Restore + focus the window (from tray, global hotkey, or a second launch).</summary>
    public void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>Show the window and create a fresh note (tray / global hotkey / Ctrl+N).</summary>
    public void NewNote()
    {
        ShowAndActivate();
        if (RootFrame.Content is MainPage mp)
            mp.CreateNewNote();
        else
            RootFrame.Navigate(typeof(MainPage), "new");
    }

    private void ApplyAlwaysOnTop(bool on, bool persist)
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.IsAlwaysOnTop = on;
        if (persist)
            App.GetService<ISettingsService>().SetBool("always_on_top", on);
    }

    // ----- tray menu handlers -----
    private void Tray_Open(object sender, RoutedEventArgs e) => ShowAndActivate();

    private void Tray_NewNote(object sender, RoutedEventArgs e) => NewNote();

    private void Tray_NewTask(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
        RootFrame.Navigate(typeof(TasksPage));
    }

    private void Tray_ToggleAlwaysOnTop(object sender, RoutedEventArgs e)
        => ApplyAlwaysOnTop(AlwaysOnTopItem.IsChecked, persist: true);

    private void Tray_TogglePause(object sender, RoutedEventArgs e)
        => App.GetService<ISettingsService>().SetBool("pause_notifications", PauseNotifyItem.IsChecked);

    private void Tray_Settings(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
        RootFrame.Navigate(typeof(SettingsPage));
    }

    // ----- title-bar quick actions -----
    private void Gear_Click(object sender, RoutedEventArgs e)
    {
        if (RootFrame.Content is not SettingsPage)
            RootFrame.Navigate(typeof(SettingsPage));
    }

    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_pinSync)
            return;
        var on = PinToggle.IsChecked == true;
        ApplyAlwaysOnTop(on, persist: true);
        AlwaysOnTopItem.IsChecked = on;
    }

    // ----- global AI assistant ("ball") -----
    private bool _aiOpen;
    private readonly List<string> _aiHistory = new();   // running transcript for multi-turn context
    private ISpeechToTextService? _aiStt;
    private MicCaptureService? _aiMic;

    /// <summary>Size the floating chat panel to fit inside the window at any width/height.</summary>
    private void AdjustAiPanel(double width, double height)
    {
        if (AiPanel is null)
            return;
        AiPanel.Width = Math.Clamp(width - 48, 260, 360);          // 24px margin each side
        AiPanel.MaxHeight = Math.Max(260, height - 100);            // leave room for the title bar
    }

    private void AiBall_Click(object sender, RoutedEventArgs e) => OpenAi();

    private void AiClose_Click(object sender, RoutedEventArgs e) => CloseAi();

    private void OpenAi()
    {
        if (_aiOpen)
            return;
        _aiOpen = true;
        AiPanel.Visibility = Visibility.Visible;
        AiPanel.IsHitTestVisible = true;
        AiBall.IsHitTestVisible = false;
        ((Storyboard)RootGrid.Resources["AiOpenStoryboard"]).Begin();
        AiInput.Focus(FocusState.Programmatic);
    }

    private void CloseAi()
    {
        if (!_aiOpen)
            return;
        _aiOpen = false;
        AiPanel.IsHitTestVisible = false;
        AiBall.IsHitTestVisible = true;
        _ = StopAiDictationAsync();
        ((Storyboard)RootGrid.Resources["AiCloseStoryboard"]).Begin();   // Completed → collapse
    }

    private void AiInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = PlanAsync();
        }
    }

    private async void AiSend_Click(object sender, RoutedEventArgs e) => await PlanAsync();

    private async System.Threading.Tasks.Task PlanAsync()
    {
        var svc = App.GetService<AiAssistService>();
        var text = AiInput.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            AiStatus.Text = "Type or say what you'd like me to do.";
            return;
        }
        if (!svc.IsConfigured)
        {
            AiStatus.Text = $"AI provider '{svc.Provider}' has no key. Add VERTEX_AI_API_KEY or GEMINI_API_KEY to .env (or set AI_PROVIDER=fake).";
            return;
        }

        AppendChatBubble(text, fromUser: true);
        _aiHistory.Add("User: " + text);
        AiInput.Text = "";
        // Show "Thinking…" as the next line of the conversation (below the user's message), not above.
        var thinking = AppendChatBubble("Thinking…", fromUser: false);
        ScrollAiToEnd();

        try
        {
            var plan = await svc.PlanAsync(string.Join("\n", _aiHistory));
            AiConversation.Children.Remove(thinking);
            if (!string.IsNullOrWhiteSpace(plan.Reply))
            {
                AppendChatBubble(plan.Reply, fromUser: false);
                _aiHistory.Add("Assistant: " + plan.Reply);
            }

            if (plan.Actions.Count > 0)
            {
                // Act by default — apply immediately, then show what was created.
                long? noteId = (RootFrame.Content as MainPage)?.CurrentNoteIdOrNull;
                var summary = svc.Apply(plan.Actions, noteId);
                AiConversation.Children.Add(new TextBlock
                {
                    Text = "✓ " + summary,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 0, 0, 2),
                    Foreground = (Brush)Application.Current.Resources["AppTextSecondaryBrush"],
                });
                AiStatus.Text = summary;             // starts with "Applied"
                _aiHistory.Add("Assistant: " + summary);
                RefreshAfterAi();
            }
            else
            {
                AiStatus.Text = "";   // a clarifying question — wait for the user's reply
            }
            ScrollAiToEnd();
        }
        catch (Exception ex)
        {
            AiConversation.Children.Remove(thinking);
            AppendChatBubble("AI error: " + ex.Message, fromUser: false);
            AiStatus.Text = "AI error: " + ex.Message;
            ScrollAiToEnd();
        }
    }

    /// <summary>Append a chat bubble: user bubbles right + accent, assistant bubbles left + subtle.
    /// Returns the bubble so transient ones (e.g. "Thinking…") can be removed.</summary>
    private Border AppendChatBubble(string text, bool fromUser)
    {
        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources[fromUser ? "AppAccentBrush" : "AppHoverBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 6, 10, 6),
            MaxWidth = 270,
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = fromUser ? new SolidColorBrush(Microsoft.UI.Colors.White) : (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
            },
        };
        AiConversation.Children.Add(bubble);
        return bubble;
    }

    /// <summary>Enable Send only when there's something to send.</summary>
    private void AiInput_TextChanged(object sender, TextChangedEventArgs e)
        => AiSend.IsEnabled = !string.IsNullOrWhiteSpace(AiInput.Text);

    private void ScrollAiToEnd()
    {
        AiScroll.UpdateLayout();
        AiScroll.ChangeView(null, AiScroll.ScrollableHeight, null);
    }

    private async void AiSummarize_Click(object sender, RoutedEventArgs e)
    {
        if (RootFrame.Content is not MainPage mp || !mp.HasActiveNote)
        {
            AiStatus.Text = "Open a note first to summarize it.";
            return;
        }
        AiStatus.Text = "Summarizing…";
        try { AiStatus.Text = await mp.SummarizeActiveNoteAsync(); }
        catch (Exception ex) { AiStatus.Text = "AI error: " + ex.Message; }
    }

    private async void AiRephrase_Click(object sender, RoutedEventArgs e)
    {
        if (RootFrame.Content is not MainPage mp || !mp.HasActiveNote)
        {
            AiStatus.Text = "Open a note and select some text to rephrase.";
            return;
        }
        AiStatus.Text = "Rephrasing…";
        try { AiStatus.Text = await mp.RephraseSelectionAsync(); }
        catch (Exception ex) { AiStatus.Text = "AI error: " + ex.Message; }
    }

    /// <summary>Reload whichever section is showing so AI-created content appears immediately.</summary>
    private void RefreshAfterAi()
    {
        switch (RootFrame.Content)
        {
            case MainPage mp: mp.ReloadAfterAi(); break;
            case TasksPage tp: tp.Reload(); break;
        }
    }

    // ----- AI ball dictation (shares the single-session DictationCoordinator with the editor mic) -----
    private async void AiMic_Click(object sender, RoutedEventArgs e)
    {
        if (AiMic.IsChecked == true)
            await StartAiDictationAsync();
        else
            await StopAiDictationAsync();
    }

    private async System.Threading.Tasks.Task StartAiDictationAsync()
    {
        if (_aiStt is not null)
            return;   // already listening — ignore a duplicate start

        var factory = App.GetService<ISpeechToTextFactory>();
        var stt = factory.Create();
        if (!stt.IsConfigured)
        {
            AiStatus.Text = $"Add an API key for '{factory.Provider}' in .env to dictate.";
            AiMic.IsChecked = false;
            return;
        }

        _aiStt = stt;
        await DictationCoordinator.ClaimAsync(this, StopAiDictationAsync);
        stt.PartialReceived += OnAiSttPartial;
        stt.FinalReceived += OnAiSttFinal;
        stt.ErrorOccurred += OnAiSttError;
        await stt.StartAsync(CancellationToken.None);

        // If the user toggled the mic off (or another session claimed it) during startup, stop cleanly.
        if (AiMic.IsChecked != true || !ReferenceEquals(_aiStt, stt))
        {
            await StopAiDictationAsync();
            return;
        }

        AiStatus.Text = "Listening…";
        if (stt.NeedsMicrophone)
        {
            _aiMic = new MicCaptureService();
            _aiMic.FrameReady += OnAiMicFrame;
            if (!_aiMic.Start(msg => DispatcherQueue.TryEnqueue(() => AiStatus.Text = msg)))
                await StopAiDictationAsync();
        }
    }

    private async void OnAiMicFrame(byte[] frame)
    {
        if (_aiStt is not null)
            try { await _aiStt.SendAsync(frame); } catch { /* dropped frame */ }
    }

    private void OnAiSttPartial(string text)
        => DispatcherQueue.TryEnqueue(() => AiStatus.Text = "… " + text);

    private void OnAiSttFinal(string text)
        => DispatcherQueue.TryEnqueue(() =>
        {
            AppendAiInput(text);
            AiStatus.Text = "Listening… (tap Send when ready)";
        });

    private void OnAiSttError(string message)
        => DispatcherQueue.TryEnqueue(() => AiStatus.Text = message);

    private void AppendAiInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var sep = AiInput.Text.Length == 0 || AiInput.Text.EndsWith(' ') ? "" : " ";
        AiInput.Text += sep + text.Trim();
    }

    private async System.Threading.Tasks.Task StopAiDictationAsync()
    {
        if (_aiMic is not null)
        {
            _aiMic.FrameReady -= OnAiMicFrame;
            _aiMic.Dispose();
            _aiMic = null;
        }
        if (_aiStt is not null)
        {
            _aiStt.PartialReceived -= OnAiSttPartial;
            _aiStt.FinalReceived -= OnAiSttFinal;
            _aiStt.ErrorOccurred -= OnAiSttError;
            try { await _aiStt.StopAsync(); } catch { /* best effort */ }
            _aiStt = null;
        }
        DictationCoordinator.Release(this);
        if (AiMic.IsChecked == true)
            AiMic.IsChecked = false;
        if (AiStatus.Text == "Listening…" || AiStatus.Text.StartsWith("… "))
            AiStatus.Text = "";   // clear the listening indicator so it doesn't stick
    }

    private void Tray_Exit(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        SaveWindowState();
        _reminderTimer?.Stop();
        _hotkey?.Dispose();
        TrayIcon.Dispose();
        Application.Current.Exit();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>On launch: check GitHub Releases and offer a one-click download + install if newer.</summary>
    private async System.Threading.Tasks.Task StartupUpdateCheckAsync()
    {
        try
        {
            // Skip the network check (and its modal dialog) under UI automation so tests are
            // deterministic and a dev build's lower version doesn't pop a blocking dialog.
            if (Environment.GetEnvironmentVariable("CacheNote_NO_UPDATE_CHECK") == "1")
                return;

            var svc = App.GetService<GitHubUpdateService>();
            if (!svc.IsConfigured)
                return;
            var result = await svc.CheckAsync();
            if (!result.Available || string.IsNullOrEmpty(result.DownloadUrl))
                return;

            // Don't nag on every launch: once the user dismisses a given version, stay quiet for it
            // (the title-bar Update button still updates on demand). A newer release re-prompts.
            var settings = App.GetService<ISettingsService>();
            if (settings.Get(SkippedUpdateKey, "") == result.LatestVersion)
                return;

            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = $"CacheNote {result.LatestVersion} is available (you have {svc.CurrentVersion}). Download and install now?",
                PrimaryButtonText = "Update now",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
            };
            if (await DialogHost.ShowAsync(dialog) == ContentDialogResult.Primary)
                await svc.DownloadAndRunAsync(result.DownloadUrl!);
            else
                settings.Set(SkippedUpdateKey, result.LatestVersion);   // remember the dismissal
        }
        catch { /* never block startup on the update check */ }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var svc = App.GetService<GitHubUpdateService>();
        var panel = new StackPanel { Width = 300, Spacing = 8 };
        // Show the installed version immediately so the dialog is informative before the network
        // check returns (and so it doesn't depend on GitHub latency).
        var status = new TextBlock { Text = $"Installed version: {svc.CurrentVersion}\n\nChecking for updates…", TextWrapping = TextWrapping.Wrap };
        status.SetValue(AutomationProperties.AutomationIdProperty, "UpdateStatus");
        panel.Children.Add(status);

        var dialog = new ContentDialog
        {
            Title = "CacheNote updates",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = RootGrid.XamlRoot,
        };
        _ = DialogHost.ShowAsync(dialog);

        var result = await svc.CheckAsync();
        status.Text = $"Installed version: {svc.CurrentVersion}\n\n{result.Message}";

        if (result.Available && !string.IsNullOrEmpty(result.DownloadUrl))
        {
            var update = new Button { Content = $"Download & install {result.LatestVersion}" };
            update.Click += async (_, _) =>
            {
                update.IsEnabled = false;
                status.Text = "Downloading the installer…";
                var ok = await svc.DownloadAndRunAsync(result.DownloadUrl!);
                status.Text = ok ? "Installer launched — CacheNote will update." : "Download failed. Try again later.";
            };
            panel.Children.Add(update);
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        // Treat the current effective theme as the baseline, then flip it.
        var current = RootGrid.ActualTheme;
        ApplyTheme(current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark, persist: true);
    }

    // ----- public hooks for the Settings page (window-level actions) -----
    public void SetTheme(ElementTheme theme) => ApplyTheme(theme, persist: true);

    public void SetAlwaysOnTop(bool on)
    {
        ApplyAlwaysOnTop(on, persist: true);
        AlwaysOnTopItem.IsChecked = on;
        _pinSync = true; PinToggle.IsChecked = on; _pinSync = false;
    }

    public void SetPauseNotifications(bool on)
    {
        App.GetService<ISettingsService>().SetBool("pause_notifications", on);
        PauseNotifyItem.IsChecked = on;
    }

    /// <summary>Shrink to a small always-handy note window (~square).</summary>
    public void EnterCompactMode() => AppWindow.Resize(new SizeInt32(380, 480));

    public void DockLeft() => DockHalf(left: true);

    public void DockRight() => DockHalf(left: false);

    private void DockHalf(bool left)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is null)
            return;
        var wa = area.WorkArea;
        var w = wa.Width / 2;
        AppWindow.MoveAndResize(new RectInt32(left ? wa.X : wa.X + wa.Width - w, wa.Y, w, wa.Height));
    }

    /// <summary>Return to the default centered window size.</summary>
    public void RestoreWindow()
    {
        AppWindow.Resize(new SizeInt32(1100, 720));
        CenterOnScreen();
    }

    private void ApplyTheme(ElementTheme theme, bool persist)
    {
        RootGrid.RequestedTheme = theme;

        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && RootGrid.ActualTheme == ElementTheme.Dark);
        ThemeIcon.Glyph = isDark ? SunGlyph : MoonGlyph;

        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
        bar.ButtonHoverForegroundColor = isDark ? Colors.White : Colors.Black;

        if (persist)
            App.GetService<ISettingsService>().Set(ThemeKey, theme.ToString());
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (area is null)
            return;

        var work = area.WorkArea;
        var x = work.X + (work.Width - AppWindow.Size.Width) / 2;
        var y = work.Y + (work.Height - AppWindow.Size.Height) / 2;
        AppWindow.Move(new PointInt32(x, y));
    }

    private bool RestoreWindowState()
    {
        if (TestMode)
            return false;   // always launch at the default size during automation
        var s = App.GetService<ISettingsService>();
        int w = s.GetInt("win_w"), h = s.GetInt("win_h");
        int x = s.GetInt("win_x", int.MinValue), y = s.GetInt("win_y", int.MinValue);
        if (w < 400 || h < 300 || x == int.MinValue || y == int.MinValue)
            return false;

        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));

        // Clamp onto the nearest display's work area so it can't be lost off-screen.
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            var wa = area.WorkArea;
            var cx = Math.Clamp(x, wa.X, Math.Max(wa.X, wa.X + wa.Width - w));
            var cy = Math.Clamp(y, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - h));
            if (cx != x || cy != y)
                AppWindow.Move(new PointInt32(cx, cy));
        }
        return true;
    }

    private void SaveWindowState()
    {
        if (TestMode)
            return;   // don't let one test's geometry leak into the next
        var s = App.GetService<ISettingsService>();
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        s.SetInt("win_w", size.Width);
        s.SetInt("win_h", size.Height);
        s.SetInt("win_x", pos.X);
        s.SetInt("win_y", pos.Y);
    }
}
