using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using CacheNote.Core.Data.Migrations;
using CacheNote.Core.DependencyInjection;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Logging;
using CacheNote.Core.Services;
using CacheNote_App.Services;

namespace CacheNote_App;

/// <summary>
/// Application entry point. Builds the Generic Host (DI + logging), ensures the
/// portable data folders exist, runs schema migrations, registers native toasts,
/// then shows the main window.
/// </summary>
public partial class App : Application
{
    private static readonly Stopwatch ColdStartTimer = Stopwatch.StartNew();

    /// <summary>The application-wide host. Available after the App constructor runs.</summary>
    public static IHost Host { get; private set; } = null!;

    /// <summary>Resolve a required service from the DI container.</summary>
    public static T GetService<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    private Window? _window;

    /// <summary>The main window, available to pages that need window-level actions (theme, docking).</summary>
    public static MainWindow? MainShell { get; private set; }

    public App()
    {
        InitializeComponent();
        Host = BuildHost();
    }

    private static IHost BuildHost()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });

        builder.Services.AddCacheNoteCore();
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<StartupService>();
        builder.Services.AddSingleton<GitHubUpdateService>();

        // Log to logs/app.log under the app root (single source of truth = AppPaths).
        var paths = new AppPaths();
        paths.EnsureCreated();
        builder.Logging.ClearProviders();
        builder.Logging.AddFile(paths.LogFile, LogLevel.Information);

        return builder.Build();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var log = GetService<ILogger<App>>();
        try
        {
            GetService<IAppPaths>().EnsureCreated();
            GetService<MigrationRunner>().Run();

            // Daily safety snapshot AFTER migrations (so the backup has the upgraded schema).
            // Off the UI thread — a multi-MB VACUUM INTO must not delay first paint.
            var backup = GetService<CacheNote.Core.Data.DatabaseBackupService>();
            _ = System.Threading.Tasks.Task.Run(backup.RunDailyBackup);

            // Subscribe BEFORE Register so the invocation handler is wired when toasts fire.
            var toast = GetService<ToastService>();
            toast.Activated += arg => HandleToastAction(arg, "NotificationInvoked");
            toast.Register();

            _window = new MainWindow();
            MainShell = _window as MainWindow;
            _window.Activate();

            // A toast "Open" click that cold-started the app arrived before the window existed.
            if (_pendingOpenReminders && _window is MainWindow shell)
            {
                _pendingOpenReminders = false;
                shell.NavigateToReminders();
            }

            log.LogInformation("CacheNote launched in {Elapsed} ms (cold start).",
                ColdStartTimer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Fatal error during startup.");
            throw;
        }
    }

    /// <summary>Bring the main window to the front — called when a second launch redirects here.</summary>
    public void BringToForeground()
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_window is MainWindow mw)
                mw.ShowAndActivate();
        });
    }

    /// <summary>
    /// Handle a toast button click. Both activation paths funnel here: the in-process
    /// <see cref="ToastService"/> handler (cold start / app already foreground) AND the
    /// AppInstance redirect path (app in tray — see <see cref="Program"/>), because
    /// single-instance redirects AppNotification activations to the primary process.
    /// </summary>
    public void HandleToastAction(string argument, string source)
    {
        var log = GetService<ILogger<App>>();
        log.LogInformation("Toast action via {Source}: {Argument}", source, argument);

        var args = ParseToastArgs(argument);
        args.TryGetValue("action", out var action);
        long.TryParse(args.GetValueOrDefault("id"), out var id);
        int.TryParse(args.GetValueOrDefault("min"), out var min);

        var reminders = GetService<IReminderService>();

        // Complete/Snooze are DB-only — run them immediately, NOT via the window dispatcher.
        // When the app is cold-started by a toast click this fires while _window is still null,
        // and dispatcher-routing silently dropped the action (the reminder stayed active even
        // though the user clicked "Complete").
        switch (action)
        {
            case "complete":
                reminders.Complete(id);
                break;
            case "snooze":
                reminders.Snooze(id, min <= 0 ? 5 : min, DateTime.UtcNow);
                break;
            default:
                if (_window is null)
                    _pendingOpenReminders = true;   // window not built yet — navigate after launch
                break;
        }

        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            switch (action)
            {
                case "complete":
                case "snooze":
                    RefreshRemindersUi();
                    break;
                default: // "open", or the toast body itself
                    if (_window is MainWindow mw)
                    {
                        mw.ShowAndActivate();
                        mw.NavigateToReminders();
                    }
                    break;
            }
        });
    }

    private bool _pendingOpenReminders;

    private void RefreshRemindersUi()
    {
        if (_window is MainWindow mw)
            mw.RefreshRemindersIfOpen();
    }

    private static Dictionary<string, string> ParseToastArgs(string argument)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in argument.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2)
                dict[kv[0]] = kv[1];
        }
        return dict;
    }
}
