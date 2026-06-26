using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CacheNote_App.Interop;

/// <summary>
/// Registers one or more system-wide hotkeys (e.g. Ctrl+Shift+N → new note, Ctrl+Alt+C → open) by
/// subclassing the window's WndProc once and listening for <c>WM_HOTKEY</c>. Each binding gets a
/// distinct hotkey id so they don't collide. The replacement WndProc delegate is held in a field so
/// the GC cannot collect it while Windows still holds the function pointer — a collected delegate is
/// the classic cause of subclass crashes.
/// </summary>
/// <remarks>Verified on win-x64. win-x86 uses the SetWindowLongW fallback (untested here).</remarks>
public sealed class GlobalHotkey : IDisposable
{
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModAlt = 0x0001;

    private const int WM_HOTKEY = 0x0312;
    private const int GWLP_WNDPROC = -4;
    private const uint MOD_NOREPEAT = 0x4000;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly IntPtr _hwnd;
    private readonly WndProc _hookProc;   // kept alive deliberately (do not inline)
    private readonly Dictionary<int, Action> _actions = new();
    private IntPtr _oldProc;
    private int _nextId = 0xB001;
    private bool _disposed;

    public GlobalHotkey(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hookProc = HookProc;
        _oldProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_hookProc));
    }

    /// <summary>Register a hotkey. Returns false when another app already owns this combination
    /// (registration silently fails) — the caller can surface that to the user.</summary>
    public bool Register(uint modifiers, uint virtualKey, Action onPressed)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, virtualKey))
            return false;
        _actions[id] = onPressed;
        return true;
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
            action();
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var id in _actions.Keys)
            UnregisterHotKey(_hwnd, id);
        _actions.Clear();

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
