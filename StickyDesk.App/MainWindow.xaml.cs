using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using StickyDesk.Core.Services;
using StickyDesk_App.Services;
using StickyDesk_App.Interop;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace StickyDesk_App;

/// <summary>
/// The application window: custom title bar (Mica), a theme toggle in the title-bar
/// footer, and the content Frame. Window-state persistence lands in M1b.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string ThemeKey = "theme";

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

        RootFrame.Navigate(typeof(HomePage));

        // Restore the saved theme (defaults to following the system).
        var saved = App.GetService<ISettingsService>().Get(ThemeKey, nameof(ElementTheme.Default));
        ApplyTheme(Enum.TryParse<ElementTheme>(saved, out var t) ? t : ElementTheme.Default, persist: false);

        SetupTrayAndWindowBehavior();
    }

    private bool _exiting;
    private GlobalHotkey? _hotkey;

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

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var svc = App.GetService<GitHubUpdateService>();
        var panel = new StackPanel { Width = 300, Spacing = 8 };
        var status = new TextBlock { Text = "Checking for updates…", TextWrapping = TextWrapping.Wrap };
        status.SetValue(AutomationProperties.AutomationIdProperty, "UpdateStatus");
        panel.Children.Add(status);

        var dialog = new ContentDialog
        {
            Title = "StickyDesk updates",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = RootGrid.XamlRoot,
        };
        _ = dialog.ShowAsync();

        var result = await svc.CheckAsync();
        status.Text = $"{result.Message}\n\nInstalled version: {svc.CurrentVersion}";

        if (result.Available && !string.IsNullOrEmpty(result.DownloadUrl))
        {
            var update = new Button { Content = $"Download & install {result.LatestVersion}" };
            update.Click += async (_, _) =>
            {
                update.IsEnabled = false;
                status.Text = "Downloading the installer…";
                var ok = await svc.DownloadAndRunAsync(result.DownloadUrl!);
                status.Text = ok ? "Installer launched — StickyDesk will update." : "Download failed. Try again later.";
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
        var s = App.GetService<ISettingsService>();
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        s.SetInt("win_w", size.Width);
        s.SetInt("win_h", size.Height);
        s.SetInt("win_x", pos.X);
        s.SetInt("win_y", pos.Y);
    }
}
