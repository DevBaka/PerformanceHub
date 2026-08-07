using Avalonia.Input;

namespace Deskrig.Desktop.Infrastructure;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Meta = 8 }

/// <summary>Parses strings like "Ctrl+Alt+D1" or "Win+Shift+F5" into a modifier set + key - platform-neutral,
/// the Windows/X11 backends each translate the resulting <see cref="Key"/> into their own native code
/// (virtual-key / X11 keysym).</summary>
public static class HotkeyText
{
    public static bool TryParse(string? text, out HotkeyModifiers modifiers, out Key key)
    {
        modifiers = HotkeyModifiers.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" or "meta" or "super" => HotkeyModifiers.Meta,
                _ => HotkeyModifiers.None,
            };
        }
        if (modifiers == HotkeyModifiers.None) return false;

        return Enum.TryParse(parts[^1], ignoreCase: true, out key) && key != Key.None;
    }

    public static bool IsValid(string? text) => TryParse(text, out _, out _);

    /// <summary>A-Z / 0-9 as a plain char - the one part of the key set both the Windows virtual-key table
    /// and the X11 keysym table can derive arithmetically instead of needing a giant lookup table.</summary>
    public static bool TryGetLetterOrDigit(Key key, out char ch)
    {
        if (key is >= Key.A and <= Key.Z) { ch = (char)('A' + (key - Key.A)); return true; }
        if (key is >= Key.D0 and <= Key.D9) { ch = (char)('0' + (key - Key.D0)); return true; }
        ch = '\0';
        return false;
    }

    /// <summary>F1-F24 as the trailing number.</summary>
    public static bool TryGetFunctionKeyNumber(Key key, out int n)
    {
        if (key is >= Key.F1 and <= Key.F24) { n = key - Key.F1 + 1; return true; }
        n = 0;
        return false;
    }
}
