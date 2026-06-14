using System;
using Microsoft.Win32;

namespace PerformanceHub.Utils
{
    public static class AutostartUtil
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "PerformanceHub";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (key == null) return false;
                var val = key.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(val);
            }
            catch { return false; }
        }

        public static bool TrySetEnabled(bool enable, string exePath, out string? error)
        {
            error = null;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (key == null) { error = "Failed to open Run key"; return false; }
                if (enable)
                {
                    var value = $"\"{exePath}\" --minimized";
                    key.SetValue(ValueName, value);
                }
                else
                {
                    if (key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message; return false;
            }
        }
    }
}
