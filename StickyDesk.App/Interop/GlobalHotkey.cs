using System.Runtime.InteropServices;

namespace StickyDesk_App.Interop;

/// <summary>
/// Registers a system-wide hotkey (used for Ctrl+Shift+N → new note) by subclassing
/// the window's WndProc and listening for <c>WM_HOTKEY</c>. The replacement WndProc
/// delegate is held in a field so the GC cannot collect it while Windows still holds
/// the function pointer — a collected delegate is the classic cause of subclass crashes.
/// </summary>
/// <remarks>Verified on win-x64. win-x86 uses the SetWindowLongW fallback (untested here).</remarks>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int GWLP_WNDPROC = -4;
    private const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0xB001;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly IntPtr _hwnd;
    private readonly Action _onPressed;
    private readonly WndProc _hookProc;   // kept alive deliberately (do not inline)
    private IntPtr _oldProc;
    private bool _disposed;

    public GlobalHotkey(IntPtr hwnd, uint virtualKey, Action onPressed)
    {
        _hwnd = hwnd;
        _onPressed = onPressed;
        _hookProc = HookProc;

        _oldProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_hookProc));
        RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, virtualKey);
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            _onPressed();
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        UnregisterHotKey(_hwnd, HotkeyId);
        if (_oldProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldProc);
            _oldProc = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // 64-bit exports SetWindowLongPtrW; 32-bit Windows only exports SetWindowLongW.
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
}
