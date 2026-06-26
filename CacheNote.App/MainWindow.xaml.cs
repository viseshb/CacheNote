using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using CacheNote.Core.Ai;
using CacheNote.Core.Cloud;
using CacheNote.Core.Data;
using CacheNote.Core.Services;
using CacheNote.Core.Speech;
using CacheNote_App.Controls;
using CacheNote_App.Services;
using CacheNote_App.Interop;
using Windows.Graphics;
using Windows.UI.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CacheNote_App;

/// <summary>
/// The application window: custom title bar (Mica) and the content Frame.
/// CacheNote is dark-mode only (owner decision 2026-06-11) — no theme toggle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string SkippedUpdateKey = "update_skipped_version";

    // Theme toggle PARKED (owner 2026-06-11: dark mode only). Uncomment to bring back light mode.
    // private const string ThemeKey = "theme";
    // Segoe Fluent glyphs (built from code points so the source stays plain ASCII).
    // private static readonly string SunGlyph = ((char)0xE706).ToString();   // shown in dark mode → click for light
    // private static readonly string MoonGlyph = ((char)0xE708).ToString();  // shown in light mode → click for dark

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

        // Dark before the first page loads, so every control resolves dark-theme resources.
        // (Dark mode only — owner 2026-06-11. The saved-theme restore is parked below.)
        ApplyDarkTheme();
        // var saved = App.GetService<ISettingsService>().Get(ThemeKey, nameof(ElementTheme.Default));
        // ApplyTheme(Enum.TryParse<ElementTheme>(saved, out var t) ? t : ElementTheme.Default, persist: false);

        RootFrame.Navigate(typeof(HomePage));
        RootFrame.Navigated += (_, _) => RefreshAiPanelForCurrentPage();

        // When the AI panel finishes closing, fully collapse it (so its controls aren't hit-testable).
        ((Storyboard)RootGrid.Resources["AiCloseStoryboard"]).Completed += (_, _) =>
        {
            if (!_aiOpen)
                AiPanel.Visibility = Visibility.Collapsed;
        };

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

        // Tray icon: left-click opens; the context menu has the rest. The menu is a NATIVE
        // Win32 popup (H.NotifyIcon), which never raises XAML Click — Commands only.
        TrayIcon.ForceCreate();
        TrayIcon.LeftClickCommand = new RelayCommand(ShowAndActivate);
        TrayOpenItem.Command = new RelayCommand(ShowAndActivate);
        TrayNewNoteItem.Command = new RelayCommand(NewNote);
        TrayNewTaskItem.Command = new RelayCommand(() => { ShowAndActivate(); RootFrame.Navigate(typeof(TasksPage)); });
        // Toggles flip from the persisted source of truth — the native menu's IsChecked
        // handling is not part of the XAML invoke pipeline, so it can't be trusted here.
        AlwaysOnTopItem.Command = new RelayCommand(
            () => SetAlwaysOnTop(!App.GetService<ISettingsService>().GetBool("always_on_top")));
        PauseNotifyItem.Command = new RelayCommand(
            () => SetPauseNotifications(!App.GetService<ISettingsService>().GetBool("pause_notifications")));
        TraySettingsItem.Command = new RelayCommand(() => { ShowAndActivate(); RootFrame.Navigate(typeof(SettingsPage)); });
        TrayExitItem.Command = new RelayCommand(ExitApp);

        // Always-on-top (persisted) + reflect both toggle states in the menu.
        var alwaysOnTop = settings.GetBool("always_on_top");
        ApplyAlwaysOnTop(alwaysOnTop, persist: false);
        AlwaysOnTopItem.IsChecked = alwaysOnTop;
        _pinSync = true; PinToggle.IsChecked = alwaysOnTop; _pinSync = false;
        PauseNotifyItem.IsChecked = settings.GetBool("pause_notifications");

        // Global hotkeys, active even when the app is hidden in the tray / notification overflow:
        //   Ctrl+Shift+N → new note     Ctrl+Alt+C → just open & focus CacheNote
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hotkey = new GlobalHotkey(hwnd);
        _hotkey.Register(GlobalHotkey.ModControl | GlobalHotkey.ModShift, 0x4E /* VK_N */, () => DispatcherQueue.TryEnqueue(NewNote));
        // Non-fatal if another app owns Ctrl+Alt+C: tray left-click and Ctrl+Shift+N still open us.
        if (!_hotkey.Register(GlobalHotkey.ModControl | GlobalHotkey.ModAlt, 0x43 /* VK_C */, () => DispatcherQueue.TryEnqueue(ShowAndActivate)))
            System.Diagnostics.Debug.WriteLine("CacheNote: Ctrl+Alt+C open-hotkey already in use; skipped.");

        StartReminderEngine();
        StartGoogleSync();

        // Title collapse must also track whole-window resizes (compact mode, drag-resize).
        RootGrid.SizeChanged += (_, _) => UpdateTitleBarText();
        UpdateTitleBarText();
    }

    // ----- Google Calendar sync: at startup, after local edits (debounced in the service),
    //       and every 5 minutes so remote changes flow in without user action -----
    private void StartGoogleSync()
    {
        var sync = App.GetService<GoogleCalendarSyncService>();
        if (!sync.IsConnected)
            return;

        sync.SyncCompleted += () => DispatcherQueue.TryEnqueue(RefreshCalendarIfOpen);
        sync.RequestSync();   // catch up on remote changes made while the app was closed
    }

    /// <summary>Reload the Calendar if that page is currently shown (after a sync pull).</summary>
    public void RefreshCalendarIfOpen()
    {
        if (RootFrame.Content is CalendarPage page)
            page.Vm.Reload();
    }

    // ----- reminder engine (runs on the UI dispatcher; keeps ticking while in the tray) -----
    private DispatcherQueueTimer? _reminderTimer;
    private int _syncTick;
    private readonly HashSet<long> _inAppReminderIds = new();

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
        // Guard the whole tick: this runs every 20s for the app's lifetime (incl. tray-resident);
        // one transient SQLite error must not crash a tray app the user believes is running.
        try
        {
            // Piggyback the periodic Google sync on this 20s tick (15 ticks = 5 min).
            // Runs even when notifications are paused — pause is about toasts, not sync.
            if (++_syncTick >= 15)
            {
                _syncTick = 0;
                App.GetService<GoogleCalendarSyncService>().RequestSync();
            }

            // Paused → don't fire (and don't advance, so they fire once unpaused).
            if (App.GetService<ISettingsService>().GetBool("pause_notifications"))
                return;

            var due = App.GetService<IReminderService>().GetDueAndAdvance(DateTime.UtcNow);

            // Re-arm alerts of recurring events whose one-shot alert already fired — without
            // this, a weekly meeting's alert fires exactly once, ever.
            App.GetService<EventService>().ResyncRecurringAlerts();

            if (due.Count == 0)
                return;

            var toast = App.GetService<ToastService>();
            if (toast.CanShow)
            {
                foreach (var r in due)
                    toast.ShowReminder(r.Id, r.Message);
            }
            else
            {
                // Windows notifications are off for this app (or registration failed) —
                // surface the reminder in-app instead of dropping it silently.
                foreach (var r in due)
                    ShowInAppReminder(r.Id, r.Message);
                ShowAndActivate();
            }

            RefreshRemindersIfOpen();
        }
        catch
        {
            // Transient DB/toast failure — try again on the next tick.
        }
    }

    /// <summary>In-app reminder banner with the same actions as the toast (Complete / Snooze).</summary>
    private void ShowInAppReminder(long id, string? message)
    {
        if (!_inAppReminderIds.Add(id))
            return;

        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new FontIcon
        {
            Glyph = "\uE823",
            FontSize = 15,
            Foreground = (Brush)Application.Current.Resources["AppAccentBrush"],
        });
        title.Children.Add(new TextBlock
        {
            Text = "Reminder",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
        });
        titleRow.Children.Add(title);

        var close = new Button
        {
            MinWidth = 0,
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
        };
        ToolTipService.SetToolTip(close, "Dismiss");
        Grid.SetColumn(close, 1);
        titleRow.Children.Add(close);

        var body = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message) ? "You have a reminder." : message,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
            Margin = new Thickness(0, 8, 0, 0),
        };

        var banner = new Border
        {
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["AppSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AppBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
        };

        void CloseBanner()
        {
            _inAppReminderIds.Remove(id);
            InAppNotifyHost.Children.Remove(banner);
        }

        close.Click += (_, _) => CloseBanner();

        var actions = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(MakeReminderAction("Open", () => NavigateToReminders(), CloseBanner));
        actions.Children.Add(MakeReminderAction("Complete", () => App.GetService<IReminderService>().Complete(id), CloseBanner));
        actions.Children.Add(MakeReminderAction("Snooze 5m", () => App.GetService<IReminderService>().Snooze(id, 5, DateTime.UtcNow), CloseBanner));
        actions.Children.Add(MakeReminderAction("Snooze 15m", () => App.GetService<IReminderService>().Snooze(id, 15, DateTime.UtcNow), CloseBanner));

        var content = new StackPanel();
        content.Children.Add(titleRow);
        content.Children.Add(body);
        content.Children.Add(actions);
        banner.Child = content;

        InAppNotifyHost.Children.Add(banner);
    }

    private Button MakeReminderAction(string label, Action act, Action close)
    {
        var b = new Button
        {
            Content = label,
            FontSize = 12,
            MinWidth = 92,
            Padding = new Thickness(10, 5, 10, 5),
        };
        b.Click += (_, _) =>
        {
            try { act(); } catch { /* DB hiccup — banner still closes; next poll re-fires if needed */ }
            close();
            RefreshRemindersIfOpen();
        };
        return b;
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

    /// <summary>Collapse the title to "CN" only when the drag region is too narrow for
    /// "CacheNote" (≈75px at 13px SemiBold + breathing room) — otherwise it overlaps the
    /// neighbor buttons. Evaluated from BOTH the drag-region Border and the root grid:
    /// the Border alone missed resizes where its own SizeChanged didn't re-fire.</summary>
    private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateTitleBarText();

    private void UpdateTitleBarText()
        => AppTitleText.Text = AppTitleBar.ActualWidth < 120 ? "CN" : "CacheNote";

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
    private Border? _activeRephraseCard;
    private TextBlock? _activeRephraseText;
    private string _pendingRephrase = "";

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
        RefreshAiPanelForCurrentPage();
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

    private void RefreshAiPanelForCurrentPage()
    {
        if (AiQuickActions is null)
            return;

        var onNotes = RootFrame.Content is MainPage;
        AiQuickActions.Visibility = onNotes ? Visibility.Visible : Visibility.Collapsed;

        if (!_aiOpen)
            return;

        AiStatus.Text = RootFrame.Content switch
        {
            MainPage => "Ask about this note, summarize it, or review a selected rewrite here.",
            TasksPage => "Ask about tasks, due dates, priorities, or ask me to create a task.",
            RemindersPage => "Ask about reminders, upcoming nudges, or ask me to schedule one.",
            CalendarPage => "Ask about your calendar, meetings, agenda, or ask me to add an event.",
            FavoritesPage => "Ask about pinned and favorite notes, or ask me to create/update notes.",
            SettingsPage => "Ask about app setup, AI keys, notifications, or preferences.",
            _ => "Ask me about CacheNote, or tell me what to create, edit, or schedule.",
        };
    }

    private void AiInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (IsShiftDown())
                return;
            e.Handled = true;
            _ = PlanAsync();
        }
    }

    private static bool IsShiftDown()
    {
        var left = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.LeftShift);
        var right = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.RightShift);
        var generic = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
        return left.HasFlag(CoreVirtualKeyStates.Down) ||
               right.HasFlag(CoreVirtualKeyStates.Down) ||
               generic.HasFlag(CoreVirtualKeyStates.Down);
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

        if (TryHandleVagueActionRequest(text))
        {
            ScrollAiToEnd();
            return;
        }

        if (await TryHandleChatOnlyRequestAsync(text))
        {
            ScrollAiToEnd();
            return;
        }
        // Show "Thinking…" as the next line of the conversation (below the user's message), not above.
        var thinking = AppendChatBubble("Thinking…", fromUser: false);
        ScrollAiToEnd();

        try
        {
            var plan = await svc.PlanAsync(string.Join("\n", _aiHistory), await BuildAiContextAsync());
            AiConversation.Children.Remove(thinking);
            if (!string.IsNullOrWhiteSpace(plan.Reply))
            {
                AppendChatBubble(plan.Reply, fromUser: false);
                _aiHistory.Add("Assistant: " + plan.Reply);
            }

            if (plan.Actions.Count > 0)
            {
                // Capture the note the plan was made against now, so a confirm card the user answers
                // later still targets that note even if they've navigated to another section.
                var planSection = CurrentSectionName();
                long? planNoteId = (RootFrame.Content as MainPage)?.CurrentNoteIdOrNull;

                // Archiving/deleting the open note is hard to undo — confirm before applying the batch.
                if (plan.Actions.Any(a => a.IsDestructive))
                    ConfirmThenApplyPlan(plan, planSection, planNoteId);
                else
                    ApplyPlan(plan, planSection, planNoteId);
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

    /// <summary>Apply by default — run the actions immediately, report, and offer to jump to the result.
    /// <paramref name="noteId"/> is the note the plan was made against (captured at plan time).</summary>
    private void ApplyPlan(AiPlan plan, string fromSection, long? noteId)
    {
        var svc = App.GetService<AiAssistService>();
        var summary = svc.Apply(plan.Actions, noteId);
        var createdNoteId = svc.LastCreatedNoteId;
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
        OfferRedirectIfUseful(plan.Actions, fromSection, createdNoteId);
        ScrollAiToEnd();
    }

    /// <summary>Show a Yes/No card before applying a plan that would archive or delete the open note.</summary>
    private void ConfirmThenApplyPlan(AiPlan plan, string fromSection, long? noteId)
    {
        var verb = plan.Actions.Any(a => a.Action == AiActionKinds.SetCurrentNoteState && a.Deleted == true)
            ? "delete" : "archive";

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"This will {verb} the current note. Apply?",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var yes = new Button { Content = "Yes", MinWidth = 64 };
        var no = new Button { Content = "No", MinWidth = 64 };
        row.Children.Add(yes);
        row.Children.Add(no);
        panel.Children.Add(row);

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["AppHoverBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            MaxWidth = 290,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel,
        };

        yes.Click += (_, _) =>
        {
            AiConversation.Children.Remove(card);
            ApplyPlan(plan, fromSection, noteId);
        };
        no.Click += (_, _) =>
        {
            AiConversation.Children.Remove(card);
            AiStatus.Text = "Cancelled — your note was not changed.";
        };

        AiConversation.Children.Add(card);
        AiStatus.Text = $"Confirm to {verb} the current note.";
    }

    private async System.Threading.Tasks.Task<bool> TryHandleChatOnlyRequestAsync(string text)
    {
        if (RootFrame.Content is MainPage mp && mp.HasActiveNote && AiIntent.IsSummaryRequest(text))
        {
            AiStatus.Text = "Summarizing...";
            var summary = await mp.PreviewSummaryActiveNoteAsync();
            AppendChatBubble("Summary:\n" + summary, fromUser: false);
            AiStatus.Text = "Summary shown here. Your note was not changed.";
            return true;
        }

        if (RootFrame.Content is MainPage notePage && notePage.HasActiveNote && AiIntent.IsRephraseRequest(text))
        {
            AiStatus.Text = "Rephrasing...";
            var proposal = await notePage.PreviewRephraseSelectionAsync(text);
            if (!proposal.HasSelection)
            {
                AiStatus.Text = proposal.Message;
                return true;
            }
            ShowRephraseProposal(proposal.Text);
            AiStatus.Text = proposal.Message;
            return true;
        }

        if (!AiIntent.IsReadOnlyRequest(text))
            return false;

        AiStatus.Text = "Thinking...";
        var answer = await App.GetService<AiAssistService>().AnswerAsync(text, await BuildAiContextAsync());
        AppendChatBubble(answer, fromUser: false);
        _aiHistory.Add("Assistant: " + answer);
        AiStatus.Text = "";
        return true;
    }

    private bool TryHandleVagueActionRequest(string text)
    {
        string? question = null;

        if (AiIntent.IsBareCreateRequest(text, "note", "notes"))
            question = "What should the note be called, and what should I put in it?";
        else if (AiIntent.IsBareCreateRequest(text, "task", "todo", "to-do"))
            question = "What task should I create?";
        else if (AiIntent.IsBareCreateRequest(text, "reminder", "reminders"))
            question = "What should I remind you about, and when?";
        else if (AiIntent.IsBareCreateRequest(text, "event", "calendar event", "meeting", "appointment"))
            question = "What event should I add, and when should it happen?";

        if (question is null)
            return false;

        AppendChatBubble(question, fromUser: false);
        _aiHistory.Add("Assistant: " + question);
        AiStatus.Text = "I need one detail before I change the app.";
        return true;
    }

    private void OfferRedirectIfUseful(IReadOnlyList<AiAction> actions, string fromSection, long? createdNoteId)
    {
        var destination = ResolveDestination(actions, createdNoteId);
        if (destination is null || string.Equals(destination.Section, fromSection, StringComparison.OrdinalIgnoreCase))
            return;

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Open {destination.Label} now?",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
        });

        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var yes = new Button { Content = "Yes", MinWidth = 64 };
        var no = new Button { Content = "No", MinWidth = 64 };
        actionsRow.Children.Add(yes);
        actionsRow.Children.Add(no);
        panel.Children.Add(actionsRow);

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["AppHoverBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            MaxWidth = 290,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel,
        };

        yes.Click += (_, _) =>
        {
            NavigateToDestination(destination);
            AiConversation.Children.Remove(card);
            AiStatus.Text = $"Opened {destination.Label}.";
        };
        no.Click += (_, _) =>
        {
            AiConversation.Children.Remove(card);
            AiStatus.Text = "";
        };

        AiConversation.Children.Add(card);
    }

    private AiDestination? ResolveDestination(IReadOnlyList<AiAction> actions, long? createdNoteId)
    {
        foreach (var action in actions)
        {
            switch (action.Action)
            {
                case AiActionKinds.CreateNote:
                    return new AiDestination("Notes", "the new note", typeof(MainPage), createdNoteId);
                case AiActionKinds.UpdateCurrentNote:
                case AiActionKinds.AppendToCurrentNote:
                case AiActionKinds.SetCurrentNoteState:
                case AiActionKinds.AddChecklist:
                case AiActionKinds.AddTag:
                    return new AiDestination("Notes", "Notes", typeof(MainPage), (RootFrame.Content as MainPage)?.CurrentNoteIdOrNull);
                case AiActionKinds.CreateTask:
                    return new AiDestination("Tasks", "Tasks", typeof(TasksPage), null);
                case AiActionKinds.CreateReminder:
                    return new AiDestination("Reminders", "Reminders", typeof(RemindersPage), null);
                case AiActionKinds.CreateEvent:
                    return new AiDestination("Calendar", "Calendar", typeof(CalendarPage), null);
            }
        }
        return null;
    }

    private void NavigateToDestination(AiDestination destination)
    {
        if (destination.PageType == typeof(MainPage) && destination.NoteId is long noteId)
            RootFrame.Navigate(typeof(MainPage), noteId);
        else
            RootFrame.Navigate(destination.PageType);
    }

    private string CurrentSectionName() => SectionNameFor(RootFrame.Content);

    /// <summary>Single source of truth mapping a page to its section label (used by context + redirect).</summary>
    private static string SectionNameFor(object? content) => content?.GetType().Name switch
    {
        nameof(MainPage) => "Notes",
        nameof(TasksPage) => "Tasks",
        nameof(RemindersPage) => "Reminders",
        nameof(CalendarPage) => "Calendar",
        nameof(FavoritesPage) => "Favorites",
        nameof(SettingsPage) => "Settings",
        nameof(HomePage) => "Home",
        _ => "Unknown",
    };

    private sealed record AiDestination(string Section, string Label, Type PageType, long? NoteId);

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
        try
        {
            var summary = await mp.PreviewSummaryActiveNoteAsync();
            AppendChatBubble("Summary:\n" + summary, fromUser: false);
            AiStatus.Text = "Summary shown here. Your note was not changed.";
            ScrollAiToEnd();
        }
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
        try
        {
            var proposal = await mp.PreviewRephraseSelectionAsync();
            if (!proposal.HasSelection)
            {
                AiStatus.Text = proposal.Message;
                return;
            }
            ShowRephraseProposal(proposal.Text);
            AiStatus.Text = proposal.Message;
        }
        catch (Exception ex) { AiStatus.Text = "AI error: " + ex.Message; }
    }

    private void ShowRephraseProposal(string rephrased)
    {
        RemoveActiveRephraseCard();
        _pendingRephrase = rephrased;

        var text = new TextBlock
        {
            Text = rephrased,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["AppTextPrimaryBrush"],
        };
        _activeRephraseText = text;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var accept = new Button { Content = "✓", MinWidth = 36 };
        var reject = new Button { Content = "×", MinWidth = 36 };
        var other = new Button { Content = "Other", MinWidth = 72 };
        ToolTipService.SetToolTip(accept, "Replace selected text");
        ToolTipService.SetToolTip(reject, "Keep original text");
        ToolTipService.SetToolTip(other, "Describe how to rephrase it");
        actions.Children.Add(accept);
        actions.Children.Add(reject);
        actions.Children.Add(other);

        var instruction = new TextBox
        {
            AcceptsReturn = true,
            PlaceholderText = "Tell me how to make it better...",
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0),
            MinHeight = 64,
            MaxHeight = 120,
            TextWrapping = TextWrapping.Wrap,
        };
        instruction.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        instruction.SetValue(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Auto);

        accept.Click += (_, _) => ApplyPendingRephrase();
        reject.Click += (_, _) =>
        {
            RemoveActiveRephraseCard();
            AiStatus.Text = "Original kept. Tell me how I can do better.";
            AiInput.Focus(FocusState.Programmatic);
        };
        other.Click += (_, _) =>
        {
            instruction.Visibility = Visibility.Visible;
            instruction.Focus(FocusState.Programmatic);
        };
        instruction.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
                return;
            if (IsShiftDown())
                return;
            e.Handled = true;
            await RegenerateRephraseAsync(instruction.Text);
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Rephrased text",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AppTextSecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 6),
        });
        content.Children.Add(text);
        content.Children.Add(actions);
        content.Children.Add(instruction);

        _activeRephraseCard = new Border
        {
            Background = (Brush)Application.Current.Resources["AppHoverBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            MaxWidth = 290,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
        AiConversation.Children.Add(_activeRephraseCard);
        ScrollAiToEnd();
    }

    private async System.Threading.Tasks.Task RegenerateRephraseAsync(string instruction)
    {
        if (RootFrame.Content is not MainPage mp || string.IsNullOrWhiteSpace(instruction))
            return;
        AiStatus.Text = "Rephrasing with your note...";
        try
        {
            var proposal = await mp.PreviewRephraseSelectionAsync(instruction);
            if (!proposal.HasSelection)
            {
                AiStatus.Text = proposal.Message;
                return;
            }
            _pendingRephrase = proposal.Text;
            if (_activeRephraseText is not null)
                _activeRephraseText.Text = proposal.Text;
            AiStatus.Text = "Review the updated rephrase.";
            ScrollAiToEnd();
        }
        catch (Exception ex) { AiStatus.Text = "AI error: " + ex.Message; }
    }

    private void ApplyPendingRephrase()
    {
        if (RootFrame.Content is not MainPage mp || string.IsNullOrWhiteSpace(_pendingRephrase))
        {
            AiStatus.Text = "Select some text in the note to rephrase.";
            return;
        }
        if (mp.ApplyRephraseSelection(_pendingRephrase))
        {
            RemoveActiveRephraseCard();
            AiStatus.Text = "Selection replaced.";
        }
        else
        {
            AiStatus.Text = "Select some text in the note to rephrase.";
        }
    }

    private void RemoveActiveRephraseCard()
    {
        if (_activeRephraseCard is not null)
            AiConversation.Children.Remove(_activeRephraseCard);
        _activeRephraseCard = null;
        _activeRephraseText = null;
        _pendingRephrase = "";
    }

    /// <summary>Reload whichever section is showing so AI-created content appears immediately.</summary>
    private void RefreshAfterAi()
    {
        switch (RootFrame.Content)
        {
            case MainPage mp: mp.ReloadAfterAi(); break;
            case TasksPage tp: tp.Reload(); break;
            case RemindersPage rp: rp.Vm.Load(); break;
            case CalendarPage cp: cp.Vm.Reload(); break;
            case FavoritesPage fp: fp.Vm.Load(); break;
        }
    }

    /// <summary>Build the assistant's context string. The open-note part touches the editor (UI thread);
    /// the app-data part is pure DB reads, so it runs off the UI thread to keep the panel responsive.</summary>
    private async System.Threading.Tasks.Task<string> BuildAiContextAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CacheNote can create and edit notes, tags, checklists, tasks, reminders, calendar events, favorites, pinned notes, archived notes, deleted notes, title colors, and notification/reminder schedules.");
        sb.AppendLine("Current section: " + SectionNameFor(RootFrame.Content));
        if (RootFrame.Content is MainPage mp)
            sb.Append(mp.GetAiContext());   // reads EditorBox — must stay on the UI thread
        sb.Append(await System.Threading.Tasks.Task.Run(BuildAppDataContext));
        return sb.ToString();
    }

    private static string BuildAppDataContext()
    {
        var sb = new StringBuilder();
        try
        {
            var notes = App.GetService<INoteRepository>().GetAllActive().Take(6).ToList();
            if (notes.Count > 0)
            {
                sb.AppendLine("Recent notes:");
                foreach (var n in notes)
                    sb.AppendLine("- " + (string.IsNullOrWhiteSpace(n.Title) ? "Untitled" : n.Title) + ": " + AiText.Truncate(n.ContentPlain, 140, collapseWhitespace: true));
            }

            var favorites = App.GetService<INoteRepository>().GetFavoritesAndPinned().Take(6).ToList();
            if (favorites.Count > 0)
            {
                sb.AppendLine("Pinned/favorite notes:");
                foreach (var n in favorites)
                    sb.AppendLine("- " + (string.IsNullOrWhiteSpace(n.Title) ? "Untitled" : n.Title));
            }

            var tasks = App.GetService<ITaskService>().GetAll().Take(8).ToList();
            if (tasks.Count > 0)
            {
                sb.AppendLine("Tasks:");
                foreach (var t in tasks)
                {
                    var due = t.DueUtc is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("MMM d h:mm tt") : "no due date";
                    sb.AppendLine($"- {(t.IsCompleted ? "done" : "open")}: {t.Title} ({t.Priority}, {due})");
                }
            }

            var reminders = App.GetService<IReminderService>().GetAll().Take(8).ToList();
            if (reminders.Count > 0)
            {
                sb.AppendLine("Reminders:");
                foreach (var r in reminders)
                {
                    var when = DateTime.SpecifyKind(r.EffectiveFireUtc, DateTimeKind.Utc).ToLocalTime().ToString("MMM d h:mm tt");
                    sb.AppendLine($"- {(r.IsDismissed ? "done" : "open")}: {(string.IsNullOrWhiteSpace(r.Message) ? "Reminder" : r.Message)} ({when}, {r.Repeat})");
                }
            }

            var events = App.GetService<EventService>().GetAll().Take(8).ToList();
            if (events.Count > 0)
            {
                sb.AppendLine("Calendar events:");
                foreach (var e in events)
                {
                    var when = DateTime.SpecifyKind(e.StartUtc, DateTimeKind.Utc).ToLocalTime().ToString("MMM d h:mm tt");
                    sb.AppendLine($"- {e.Title} ({e.Kind}, {when}, {e.Recurrence})");
                }
            }
        }
        catch
        {
            sb.AppendLine("Some app data could not be read for context.");
        }
        return sb.ToString();
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

        // If the user toggled the mic off (or another session claimed it) during startup, stop
        // THIS session — calling the field-based stop here would tear down whatever NEW session
        // _aiStt now points to and leave this one silently streaming (and billing).
        if (AiMic.IsChecked != true || !ReferenceEquals(_aiStt, stt))
        {
            stt.PartialReceived -= OnAiSttPartial;
            stt.FinalReceived -= OnAiSttFinal;
            stt.ErrorOccurred -= OnAiSttError;
            try { await stt.StopAsync(); } catch { /* best effort */ }
            if (ReferenceEquals(_aiStt, stt))
                await StopAiDictationAsync();   // ours → full cleanup (mic, coordinator, UI)
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

    /// <summary>Real exit (tray Exit / update install): flush pending edits, then tear down.</summary>
    private void ExitApp()
    {
        // The 600ms debounced autosave means continuous typing is unsaved — flush it
        // before the process dies or the loss window is the whole typing burst.
        try { (RootFrame.Content as MainPage)?.FlushPendingSave(); } catch { /* never block exit */ }

        _exiting = true;
        SaveWindowState();
        _reminderTimer?.Stop();
        _hotkey?.Dispose();
        TrayIcon.Dispose();

        // Application.Exit() alone is unreliable in WinUI 3 when invoked from the tray menu
        // while the window is hidden — the process stayed alive with a dead tray icon.
        // Close the window for a clean teardown (OnClosing skips its cancel via _exiting),
        // ask the app to exit, and back-stop with a hard process exit in case anything
        // (stray foreground thread, framework quirk) still keeps the process around.
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(2000);
            Environment.Exit(0);
        });
        try { Close(); } catch { /* proceed to Exit regardless */ }
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
            // Only an explicit "Later" click mutes this version. ContentDialogResult.None is ALSO
            // returned when DialogHost force-replaces this dialog with another one — recording
            // that as a skip would permanently mute a version the user never saw.
            var laterClicked = false;
            dialog.CloseButtonClick += (_, _) => laterClicked = true;
            if (await DialogHost.ShowAsync(dialog) == ContentDialogResult.Primary)
            {
                if (await svc.DownloadAndRunAsync(result.DownloadUrl!))
                    ExitApp();   // release our file locks so the installer can replace us
            }
            else if (laterClicked)
            {
                settings.Set(SkippedUpdateKey, result.LatestVersion);   // remember the dismissal
            }
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
                status.Text = ok ? "Installer launched — CacheNote will close and update." : "Download failed. Try again later.";
                if (ok)
                    ExitApp();   // OnClosing cancels WM_CLOSE (hide-to-tray), so the installer can never close us — exit ourselves
            };
            panel.Children.Add(update);
        }
    }

    // Theme toggle PARKED (owner 2026-06-11: dark mode only). Uncomment to bring back light mode.
    // private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    // {
    //     // Treat the current effective theme as the baseline, then flip it.
    //     var current = RootGrid.ActualTheme;
    //     ApplyTheme(current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark, persist: true);
    // }
    //
    // public void SetTheme(ElementTheme theme) => ApplyTheme(theme, persist: true);

    // ----- public hooks for the Settings page (window-level actions) -----
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
    public void EnterCompactMode()
    {
        // Resize takes PHYSICAL pixels; the minimum-size floor is DIP-scaled (380×520).
        // Unscaled, compact mode was clamped at 100% and a no-op at 150%+ DPI.
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        AppWindow.Resize(new SizeInt32((int)(380 * scale), (int)(520 * scale)));
    }

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
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(720 * scale)));
        CenterOnScreen();
    }

    /// <summary>CacheNote is dark-mode only: force the dark theme + matching caption buttons.</summary>
    private void ApplyDarkTheme()
    {
        RootGrid.RequestedTheme = ElementTheme.Dark;

        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Colors.White;
        bar.ButtonHoverForegroundColor = Colors.White;
    }

    // Full light/dark theme switcher PARKED (owner 2026-06-11: dark mode only).
    // private void ApplyTheme(ElementTheme theme, bool persist)
    // {
    //     // RichEditBox wipes custom font colors when its themed Foreground changes — let the
    //     // Notes page snapshot the RTF first and restore/adapt it after the swap.
    //     var notesPage = RootFrame.Content as MainPage;
    //     notesPage?.PrepareThemeSwap();
    //
    //     RootGrid.RequestedTheme = theme;
    //
    //     var isDark = theme == ElementTheme.Dark ||
    //                  (theme == ElementTheme.Default && RootGrid.ActualTheme == ElementTheme.Dark);
    //     ThemeIcon.Glyph = isDark ? SunGlyph : MoonGlyph;
    //
    //     var bar = AppWindow.TitleBar;
    //     bar.ButtonBackgroundColor = Colors.Transparent;
    //     bar.ButtonInactiveBackgroundColor = Colors.Transparent;
    //     bar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
    //     bar.ButtonHoverForegroundColor = isDark ? Colors.White : Colors.Black;
    //
    //     if (persist)
    //         App.GetService<ISettingsService>().Set(ThemeKey, theme.ToString());
    //
    //     notesPage?.FinishThemeSwap();
    // }

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
