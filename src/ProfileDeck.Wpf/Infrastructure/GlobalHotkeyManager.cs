using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ProfileDeck.Wpf.Infrastructure;

/// <summary>Registers global hotkeys (e.g. "Ctrl+Alt+D1") for the app's lifetime via a hidden message-only window.</summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    // HWND_MESSAGE - makes this a message-only window: no title bar, no taskbar entry, invisible.
    // Without this, HwndSource creates a real (if 0x0) top-level window that Windows still renders
    // with a caption/min/max/close frame, which is what showed up as the stray "ProfileDeckHotkeyWindow".
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    public GlobalHotkeyManager()
    {
        var parameters = new HwndSourceParameters("ProfileDeckHotkeyWindow") { Width = 0, Height = 0, ParentWindow = HWND_MESSAGE };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Returns false if the hotkey string is invalid or already taken by another application.</summary>
    public bool TryRegister(string hotkeyText, Action callback, out string? error)
    {
        error = null;
        if (!HotkeyText.TryParse(hotkeyText, out var modifiers, out var key))
        {
            error = $"Ungültiger Hotkey: '{hotkeyText}'.";
            return false;
        }

        var id = _nextId++;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_source.Handle, id, modifiers, (uint)vk))
        {
            error = $"Hotkey '{hotkeyText}' konnte nicht registriert werden (evtl. bereits belegt).";
            return false;
        }

        _callbacks[id] = callback;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys)
            UnregisterHotKey(_source.Handle, id);
        _callbacks.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            handled = true;
            Application.Current?.Dispatcher.BeginInvoke(callback);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public static class HotkeyText
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    /// <summary>Parses strings like "Ctrl+Alt+D1" or "Win+Shift+F5".</summary>
    public static bool TryParse(string? text, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => MOD_CONTROL,
                "alt" => MOD_ALT,
                "shift" => MOD_SHIFT,
                "win" or "windows" => MOD_WIN,
                _ => 0u,
            };
        }
        if (modifiers == 0) return false;

        return Enum.TryParse(parts[^1], ignoreCase: true, out key) && key != Key.None;
    }

    public static bool IsValid(string? text) => TryParse(text, out _, out _);
}
