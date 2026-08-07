using Microsoft.Win32;

namespace Deskrig.Core.Interop;

internal static class RegistryUtil
{
    public static bool TrySetDword(RegistryHive hive, string subKeyPath, string valueName, int value, out string? error)
    {
        error = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
            if (key == null) { error = "Registry-Schlüssel konnte nicht geöffnet/erstellt werden."; return false; }
            key.SetValue(valueName, value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static int? TryGetDword(RegistryHive hive, string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            var val = key?.GetValue(valueName);
            return val is int i ? i : (val != null ? Convert.ToInt32(val) : null);
        }
        catch { return null; }
    }

    public static bool TryDeleteValue(RegistryHive hive, string subKeyPath, string valueName, out string? error)
    {
        error = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: true);
            if (key == null) return true;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
