using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace CacheNote_App;

/// <summary>
/// Custom entry point (replaces the XAML-generated Main via the DISABLE_XAML_GENERATED_MAIN
/// constant). Enforces a single running instance: a second launch redirects its activation to
/// the first instance — which brings its window to the front — and then exits.
/// Set <c>CacheNote_NO_SINGLE_INSTANCE=1</c> to bypass; the UI test harness does this because
/// it launches the exe repeatedly and must get a fresh process each time.
/// </summary>
public static class Program
{
    private static IntPtr _redirectEventHandle = IntPtr.Zero;

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (!SingleInstanceBypassed() && RedirectedToExistingInstance())
            return 0;   // another instance handled this launch

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool SingleInstanceBypassed() =>
        Environment.GetEnvironmentVariable("CacheNote_NO_SINGLE_INSTANCE") == "1";

    private static bool RedirectedToExistingInstance()
    {
        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey("CacheNote-9F3C-SingleInstance");

        if (main.IsCurrent)
        {
            // We are the primary instance; handle activations that other launches redirect here.
            main.Activated += OnActivated;
            return false;
        }

        RedirectActivationTo(activationArgs, main);
        return true;
    }

    private static void OnActivated(object? sender, AppActivationArguments e)
    {
        var app = Application.Current as App;

        // A toast-button click while the app is in the tray arrives HERE (single-instance
        // redirects AppNotification activations to the primary) — not via NotificationInvoked.
        if (e.Kind == ExtendedActivationKind.AppNotification &&
            e.Data is AppNotificationActivatedEventArgs notification)
        {
            app?.HandleToastAction(notification.Argument, "AppInstance.Activated");
        }
        else
        {
            app?.BringToForeground();
        }
    }

    // Documented WinAppSDK pattern: redirect on a worker thread while this thread pumps COM,
    // so the async redirect can complete before we exit.
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance instance)
    {
        _redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            instance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(_redirectEventHandle);
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, new[] { _redirectEventHandle }, out _);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);
}
