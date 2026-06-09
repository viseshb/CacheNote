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
        try { _open?.Hide(); } catch { /* best effort */ }
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
