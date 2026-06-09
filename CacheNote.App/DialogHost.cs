using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace CacheNote_App;

/// <summary>
/// Serializes ContentDialogs. WinUI allows only ONE ContentDialog open at a time — a second
/// <c>ShowAsync</c> while one is open throws ("Only a single ContentDialog can be open at any
/// time"), which in an async-void handler silently fails or crashes. Routing every dialog through
/// here hides any currently-open dialog before showing the next, so opening (say) the AI assistant
/// while the startup "Update available" dialog is up just replaces it instead of breaking.
/// </summary>
internal static class DialogHost
{
    private static ContentDialog? _open;

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        // Hide() only BEGINS an async close — WinUI's one-dialog gate clears when the old
        // dialog actually finishes closing, so calling ShowAsync in the same tick can still
        // throw the very COMException this class exists to prevent. Await the close first.
        if (_open is { } previous)
        {
            var closed = new TaskCompletionSource();
            previous.Closed += (_, _) => closed.TrySetResult();
            try { previous.Hide(); } catch { closed.TrySetResult(); }
            await Task.WhenAny(closed.Task, Task.Delay(2000));   // never deadlock on a stuck dialog
        }

        _open = dialog;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            if (ReferenceEquals(_open, dialog))
                _open = null;
        }
    }
}
