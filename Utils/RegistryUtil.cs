using System;
using Microsoft.Win32;

namespace DJWinOptimizer.Utils
{
    public static class RegistryUtil
    {
        public static bool TrySetDword(RegistryHive hive, string subKeyPath, string valueName, int value, out string? error)
        {
            error = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
                if (key == null) { error = "Failed to open or create registry key"; return false; }
                key.SetValue(valueName, value, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySetString(RegistryHive hive, string subKeyPath, string valueName, string value, out string? error)
        {
            error = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
                if (key == null) { error = "Failed to open or create registry key"; return false; }
                key.SetValue(valueName, value, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryDeleteValue(RegistryHive hive, string subKeyPath, string valueName, out string? error)
        {
            error = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subKeyPath, writable: true);
                if (key == null) return true; // nothing to delete
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
}
