using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Deskrig.Desktop.Infrastructure;

/// <summary>
/// Registers global hotkeys (e.g. "Ctrl+Alt+D1") for the app's lifetime.
///
/// Windows: subclasses the main window's own HWND (WPF used a dedicated invisible message-only window via
/// HwndSource; Avalonia doesn't expose that helper, so this hooks WM_HOTKEY directly on the real window and
/// chains everything else to the original WndProc) and calls RegisterHotKey/UnregisterHotKey.
///
/// Linux: opens its own independent X11 connection (XOpenDisplay) - deliberately not reaching into
/// Avalonia's own X11 backend, which has no public/stable API for this - and uses XGrabKey on the root
/// window. Works under X11 and XWayland; under a native Wayland session there is no portable API for an
/// unprivileged app to grab a global hotkey at all, so XOpenDisplay simply fails there and every
/// registration reports one clear error instead of doing nothing silently.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly Window _owner;
    private readonly Dictionary<int, Action> _winCallbacks = new();
    private int _nextId = 1;

    public GlobalHotkeyManager(Window owner) => _owner = owner;

    public bool TryRegister(string hotkeyText, Action callback, out string? error)
    {
        error = null;
        if (!HotkeyText.TryParse(hotkeyText, out var mods, out var key))
        {
            error = $"Ungültiger Hotkey: '{hotkeyText}'.";
            return false;
        }

        if (OperatingSystem.IsWindows()) return TryRegisterWindows(hotkeyText, mods, key, callback, out error);
        if (OperatingSystem.IsLinux()) return TryRegisterX11(hotkeyText, mods, key, callback, out error);

        error = "Globale Hotkeys werden auf diesem Betriebssystem nicht unterstützt.";
        return false;
    }

    public void UnregisterAll()
    {
        UnregisterAllWindows();
        UnregisterAllX11();
    }

    public void Dispose()
    {
        UnregisterAll();
        TeardownWindowsSubclass();
        TeardownX11();
    }

    // ===================================================================== Windows =====

    private const int WM_HOTKEY = 0x0312;
    private const int GWLP_WNDPROC = -4;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    private IntPtr _hwnd;
    private IntPtr _prevWndProc;
    private WndProcDelegate? _wndProcDelegate; // kept alive for the lifetime of the subclass

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool EnsureWindowsSubclass()
    {
        if (_hwnd != IntPtr.Zero) return true;
        _hwnd = _owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero) return false;

        _wndProcDelegate = WndProc;
        _prevWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _wndProcDelegate);
        return true;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && _winCallbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            Dispatcher.UIThread.Post(callback);
            return IntPtr.Zero;
        }
        return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    private bool TryRegisterWindows(string hotkeyText, HotkeyModifiers mods, Key key, Action callback, out string? error)
    {
        error = null;
        if (!EnsureWindowsSubclass())
        {
            error = "Fenster noch nicht bereit für Hotkey-Registrierung.";
            return false;
        }
        if (!TryGetVirtualKey(key, out var vk))
        {
            error = $"Taste '{key}' wird für Hotkeys nicht unterstützt.";
            return false;
        }

        uint winMods = 0;
        if (mods.HasFlag(HotkeyModifiers.Alt)) winMods |= MOD_ALT;
        if (mods.HasFlag(HotkeyModifiers.Control)) winMods |= MOD_CONTROL;
        if (mods.HasFlag(HotkeyModifiers.Shift)) winMods |= MOD_SHIFT;
        if (mods.HasFlag(HotkeyModifiers.Meta)) winMods |= MOD_WIN;

        var id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, winMods, vk))
        {
            error = $"Hotkey '{hotkeyText}' konnte nicht registriert werden (evtl. bereits belegt).";
            return false;
        }
        _winCallbacks[id] = callback;
        return true;
    }

    private void UnregisterAllWindows()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var id in _winCallbacks.Keys) UnregisterHotKey(_hwnd, id);
        _winCallbacks.Clear();
    }

    private void TeardownWindowsSubclass()
    {
        if (_hwnd != IntPtr.Zero && _prevWndProc != IntPtr.Zero)
        {
            try { SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetDelegateForFunctionPointer<WndProcDelegate>(_prevWndProc)); }
            catch { /* best effort restore on shutdown */ }
        }
    }

    private static bool TryGetVirtualKey(Key key, out uint vk)
    {
        if (HotkeyText.TryGetLetterOrDigit(key, out var ch)) { vk = ch; return true; } // VK_0-9/A-Z == ASCII '0'-'9'/'A'-'Z'
        if (HotkeyText.TryGetFunctionKeyNumber(key, out var n)) { vk = (uint)(0x70 + (n - 1)); return true; } // VK_F1 = 0x70
        vk = 0;
        return false;
    }

    // ===================================================================== Linux / X11 =====

    private const uint ShiftMask = 1 << 0, LockMask = 1 << 1, ControlMask = 1 << 2, Mod1Mask = 1 << 3, Mod2Mask = 1 << 4, Mod4Mask = 1 << 6;
    private const int KeyPress = 2;
    private const int GrabModeAsync = 1;

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport("libX11.so.6")] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport("libX11.so.6")] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, IntPtr eventBuffer);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);

    private IntPtr _display = IntPtr.Zero;
    private IntPtr _rootWindow;
    private Thread? _x11Thread;
    private volatile bool _x11Running;
    private readonly Dictionary<(byte KeyCode, uint Modifiers), Action> _x11Callbacks = new();
    private readonly List<(byte KeyCode, uint Modifiers)> _x11Grabs = new();

    private bool EnsureX11Display(out string? error)
    {
        error = null;
        if (_display != IntPtr.Zero) return true;

        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            error = "Keine X11-Verbindung möglich - globale Hotkeys benötigen X11 oder XWayland (unter nativem Wayland nicht verfügbar).";
            return false;
        }

        _rootWindow = XDefaultRootWindow(_display);
        _x11Running = true;
        _x11Thread = new Thread(X11EventLoop) { IsBackground = true, Name = "Deskrig-HotkeyX11" };
        _x11Thread.Start();
        return true;
    }

    private bool TryRegisterX11(string hotkeyText, HotkeyModifiers mods, Key key, Action callback, out string? error)
    {
        error = null;
        if (!EnsureX11Display(out error)) return false;

        if (!TryGetKeysym(key, out var keysym))
        {
            error = $"Taste '{key}' wird für Hotkeys nicht unterstützt.";
            return false;
        }
        var keycode = XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            error = $"Taste '{key}' konnte nicht auf einen X11-Keycode abgebildet werden.";
            return false;
        }

        uint baseMods = 0;
        if (mods.HasFlag(HotkeyModifiers.Shift)) baseMods |= ShiftMask;
        if (mods.HasFlag(HotkeyModifiers.Control)) baseMods |= ControlMask;
        if (mods.HasFlag(HotkeyModifiers.Alt)) baseMods |= Mod1Mask;
        if (mods.HasFlag(HotkeyModifiers.Meta)) baseMods |= Mod4Mask;

        // NumLock/CapsLock show up as extra bits in the reported modifier state - grab every combination so
        // the hotkey still fires no matter their state, same trick every X11 global-hotkey implementation uses.
        foreach (var lockBits in new uint[] { 0, Mod2Mask, LockMask, Mod2Mask | LockMask })
        {
            var combo = baseMods | lockBits;
            XGrabKey(_display, keycode, combo, _rootWindow, true, GrabModeAsync, GrabModeAsync);
            _x11Grabs.Add((keycode, combo));
        }

        lock (_x11Callbacks) _x11Callbacks[(keycode, baseMods)] = callback;
        return true;
    }

    private void X11EventLoop()
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            while (_x11Running)
            {
                XNextEvent(_display, buffer); // blocks until an event we're grabbed for arrives
                var type = Marshal.ReadInt32(buffer, 0);
                if (type != KeyPress) continue;

                var state = (uint)Marshal.ReadInt32(buffer, 80);
                var keycode = (byte)Marshal.ReadInt32(buffer, 84);
                var relevantState = state & (ShiftMask | ControlMask | Mod1Mask | Mod4Mask);

                Action? callback;
                lock (_x11Callbacks) _x11Callbacks.TryGetValue((keycode, relevantState), out callback);
                if (callback != null) Dispatcher.UIThread.Post(callback);
            }
        }
        catch { /* display closed during shutdown */ }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private void UnregisterAllX11()
    {
        if (_display == IntPtr.Zero) return;
        foreach (var (keycode, mods) in _x11Grabs) XUngrabKey(_display, keycode, mods, _rootWindow);
        _x11Grabs.Clear();
        lock (_x11Callbacks) _x11Callbacks.Clear();
    }

    private void TeardownX11()
    {
        if (_display == IntPtr.Zero) return;
        _x11Running = false;
        try { XCloseDisplay(_display); } catch { /* best effort */ }
        _display = IntPtr.Zero;
    }

    private static bool TryGetKeysym(Key key, out IntPtr keysym)
    {
        // X11 keysyms for letters/digits are their plain ASCII codes (lowercase letters); function keys are
        // a contiguous block starting at XK_F1 = 0xFFBE.
        if (HotkeyText.TryGetLetterOrDigit(key, out var ch))
        {
            keysym = (IntPtr)(char.IsLetter(ch) ? char.ToLowerInvariant(ch) : ch);
            return true;
        }
        if (HotkeyText.TryGetFunctionKeyNumber(key, out var n))
        {
            keysym = (IntPtr)(0xFFBE + (n - 1));
            return true;
        }
        keysym = IntPtr.Zero;
        return false;
    }
}
