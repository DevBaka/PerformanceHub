using System;
using Microsoft.Win32;
using PerformanceHub.Core.Interfaces;

namespace PerformanceHub.Services
{
    public class VibranceService : IVibranceService
    {
        private readonly ILogger _log;

        public VibranceService(ILogger log)
        {
            _log = log;
        }

        public bool IsAvailable()
        {
            try
            {
                // Check if NVIDIA driver is installed by checking registry
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (key == null) return false;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var driverDesc = subKey.GetValue("DriverDesc") as string;
                    if (driverDesc != null && driverDesc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to check NVIDIA driver availability", ex);
                return false;
            }
        }

        public bool TrySetVibrance(int level, out string? error)
        {
            error = null;
            if (!IsAvailable())
            {
                error = "NVIDIA driver not found";
                return false;
            }

            if (level < 0 || level > 100)
            {
                error = "Vibrance level must be between 0 and 100";
                return false;
            }

            try
            {
                // NVIDIA Digital Vibrance is stored in the registry
                // Path: HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\XXXX\NDVibrance
                // Where XXXX is the NVIDIA display adapter number
                
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (key == null)
                {
                    error = "Could not open registry key";
                    return false;
                }

                bool success = false;
                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName, true);
                    if (subKey == null) continue;

                    var driverDesc = subKey.GetValue("DriverDesc") as string;
                    if (driverDesc != null && driverDesc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        // Set NDVibrance value (0-100)
                        subKey.SetValue("NDVibrance", level, RegistryValueKind.DWord);
                        success = true;
                        _log.Info($"Set NVIDIA Vibrance to {level} for adapter {subKeyName}");
                    }
                }

                if (!success)
                {
                    error = "No NVIDIA display adapter found";
                    return false;
                }

                // Notify system of display settings change
                NativeMethods.NotifyDisplaySettingsChange();
                
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error("Failed to set vibrance", ex);
                return false;
            }
        }

        public int? GetCurrentVibrance()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (key == null) return null;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var driverDesc = subKey.GetValue("DriverDesc") as string;
                    if (driverDesc != null && driverDesc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        var vibrance = subKey.GetValue("NDVibrance");
                        if (vibrance != null && vibrance is int)
                        {
                            return (int)vibrance;
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to get current vibrance", ex);
                return null;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

            public static void NotifyDisplaySettingsChange()
            {
                // WM_DISPLAYCHANGE = 0x007E
                SendMessage(IntPtr.Zero, 0x007E, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
