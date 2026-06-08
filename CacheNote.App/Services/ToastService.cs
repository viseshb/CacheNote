using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CacheNote_App.Services;

/// <summary>
/// Native Windows toast notifications via the Windows App SDK
/// <see cref="AppNotificationManager"/>. M0 uses this for the toast-feasibility
/// spike (the riskiest unpackaged integration): prove a toast displays with the
/// correct app identity and that its action buttons activate the app. The full
/// reminder action set is layered on in M3.
/// </summary>
public sealed class ToastService
{
    private readonly ILogger<ToastService> _log;
    private bool _registered;

    /// <summary>Raised (on a background thread) when a toast or its button is clicked.
    /// Argument is the toast's combined argument string.</summary>
    public event Action<string>? Activated;

    public ToastService(ILogger<ToastService> log) => _log = log;

    /// <summary>True once the notification COM activator registered successfully.</summary>
    public bool IsRegistered => _registered;

    /// <summary>Register the COM activator + invocation handler. Must run at startup,
    /// with the handler attached BEFORE Register(). Non-fatal: an unpackaged app may not
    /// yet have an identity (Start Menu shortcut + AUMID, added by the installer in M7),
    /// in which case registration fails gracefully and the app still runs.</summary>
    public void Register()
    {
        if (_registered)
            return;

        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            _registered = true;
            _log.LogInformation("AppNotificationManager registered.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Toast registration failed (likely missing app identity " +
                "while unpackaged). Notifications disabled until a shortcut/AUMID is present.");
        }
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        AppNotificationManager.Default.Unregister();
        _registered = false;
    }

    /// <summary>Fire a reminder toast with Open / Complete / Snooze 5 / Snooze 15 actions.
    /// Each button carries the reminder id so activation can act on it.</summary>
    public void ShowReminder(long id, string? message)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("Reminder")
                .AddText(string.IsNullOrWhiteSpace(message) ? "You have a reminder." : message)
                .AddButton(new AppNotificationButton("Open")
                    .AddArgument("action", "open").AddArgument("id", id.ToString()))
                .AddButton(new AppNotificationButton("Complete")
                    .AddArgument("action", "complete").AddArgument("id", id.ToString()))
                .AddButton(new AppNotificationButton("Snooze 5m")
                    .AddArgument("action", "snooze").AddArgument("min", "5").AddArgument("id", id.ToString()))
                .AddButton(new AppNotificationButton("Snooze 15m")
                    .AddArgument("action", "snooze").AddArgument("min", "15").AddArgument("id", id.ToString()))
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            _log.LogInformation("Reminder toast shown for id {Id}.", id);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to show reminder toast for id {Id}.", id);
        }
    }

    /// <summary>M0 spike: fire a sample toast with the app name and two action buttons.</summary>
    public void ShowSpikeToast()
    {
        var notification = new AppNotificationBuilder()
            .AddText("CacheNote")
            .AddText("M0 toast spike — native notifications work while unpackaged.")
            .AddButton(new AppNotificationButton("Open CacheNote")
                .AddArgument("action", "open"))
            .AddButton(new AppNotificationButton("Dismiss")
                .AddArgument("action", "dismiss"))
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
        _log.LogInformation("Spike toast shown (delivered={Delivered}).",
            notification.Id != 0);
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        _log.LogInformation("Toast invoked with argument: {Argument}", args.Argument);
        Activated?.Invoke(args.Argument);
    }
}
