using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StickyDesk.Core.Cloud;
using StickyDesk.Core.Infrastructure;
using StickyDesk.Core.Services;
using StickyDesk_App.Services;

namespace StickyDesk_App;

/// <summary>
/// Settings: appearance (theme, editor font size), behavior (startup, always-on-top, pause),
/// window modes (compact / dock), cloud key status (.env-backed, masked), and storage info.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private static readonly int[] FontSizes = [12, 13, 14, 15, 16, 18, 20, 24, 28];
    private bool _loading;

    private readonly ISettingsService _settings = App.GetService<ISettingsService>();
    private readonly StartupService _startup = App.GetService<StartupService>();
    private readonly CloudConnectivity _connectivity = App.GetService<CloudConnectivity>();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadState();
    }

    private void LoadState()
    {
        _loading = true;

        // Theme
        var theme = _settings.Get("theme", nameof(ElementTheme.Default));
        ThemeCombo.SelectedIndex = theme switch
        {
            nameof(ElementTheme.Light) => 1,
            nameof(ElementTheme.Dark) => 2,
            _ => 0,
        };

        // Editor font size
        FontSizeCombo.Items.Clear();
        foreach (var s in FontSizes)
            FontSizeCombo.Items.Add(new ComboBoxItem { Content = s.ToString(), Tag = s });
        var current = _settings.GetInt("editor_font_size", 16);
        FontSizeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(FontSizes, current));

        // Behavior
        StartupToggle.IsOn = _startup.IsEnabled();
        AlwaysOnTopToggle.IsOn = _settings.GetBool("always_on_top");
        PauseToggle.IsOn = _settings.GetBool("pause_notifications");

        // Cloud key status (masked)
        var cfg = App.GetService<CloudConfig>();
        SttProviderText.Text = cfg.SttProvider;
        DeepgramKeyText.Text = CloudConfig.Mask(cfg.DeepgramKey);
        AssemblyKeyText.Text = CloudConfig.Mask(cfg.AssemblyAiKey);
        AiProviderText.Text = cfg.AiProvider;
        GeminiKeyText.Text = CloudConfig.Mask(string.IsNullOrEmpty(cfg.VertexKey) ? cfg.GeminiKey : cfg.VertexKey);
        GoogleSyncStatus.Text = cfg.GoogleSyncConfigured
            ? "OAuth client found in .env — sign-in flow ships next."
            : "Not configured. Add GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET to .env.";

        // Storage
        var paths = App.GetService<IAppPaths>();
        RootPathText.Text = paths.Root;
        PortableText.Text = paths.IsPortable ? "Portable mode (.portable marker present)." : "Installed mode.";

        _loading = false;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        var theme = ThemeCombo.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        App.MainShell?.SetTheme(theme);
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        if (FontSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is int size)
            _settings.SetInt("editor_font_size", size);
    }

    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _startup.SetEnabled(StartupToggle.IsOn);
    }

    private void AlwaysOnTop_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.MainShell?.SetAlwaysOnTop(AlwaysOnTopToggle.IsOn);
    }

    private void Pause_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.MainShell?.SetPauseNotifications(PauseToggle.IsOn);
    }

    private void Compact_Click(object sender, RoutedEventArgs e) => App.MainShell?.EnterCompactMode();
    private void DockLeft_Click(object sender, RoutedEventArgs e) => App.MainShell?.DockLeft();
    private void DockRight_Click(object sender, RoutedEventArgs e) => App.MainShell?.DockRight();
    private void Restore_Click(object sender, RoutedEventArgs e) => App.MainShell?.RestoreWindow();

    // ----- Live connectivity tests (Phase 1 infra: prove the .env keys actually work) -----
    private async void TestDeepgram_Click(object sender, RoutedEventArgs e)
        => await RunTest(TestDeepgramBtn, _connectivity.TestDeepgramAsync);

    private async void TestAssembly_Click(object sender, RoutedEventArgs e)
        => await RunTest(TestAssemblyBtn, _connectivity.TestAssemblyAiAsync);

    private async void TestAi_Click(object sender, RoutedEventArgs e)
        => await RunTest(TestAiBtn, _connectivity.TestAiAsync);

    private async Task RunTest(Button btn, Func<System.Threading.CancellationToken, Task<(bool Ok, string Message)>> test)
    {
        btn.IsEnabled = false;
        ConnTestStatus.Foreground = (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
        ConnTestStatus.Text = "Testing…";
        try
        {
            var (ok, msg) = await test(default);
            ConnTestStatus.Text = msg;
            ConnTestStatus.Foreground = new SolidColorBrush(ok ? Colors.SeaGreen : Colors.IndianRed);
        }
        catch (Exception ex)
        {
            ConnTestStatus.Text = ex.Message;
            ConnTestStatus.Foreground = new SolidColorBrush(Colors.IndianRed);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private async void GoogleConnect_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.GetService<CloudConfig>();
        var msg = cfg.GoogleSyncConfigured
            ? "A Google OAuth client was found in .env. The sign-in + two-way sync flow is scaffolded and ships in the next build."
            : "To enable Google Calendar sync:\n\n1. In Google Cloud Console, enable the Calendar API and create an OAuth 2.0 \"Desktop app\" client.\n2. Add GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET to the .env file in the app folder.\n3. Restart StickyDesk and connect.";
        var dlg = new ContentDialog
        {
            Title = "Google Calendar sync",
            Content = msg,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
