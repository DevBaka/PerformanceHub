using System;
using System.Runtime.InteropServices;

namespace DJWinOptimizer.Utils
{
    internal static class Win32Native
    {
        [DllImport("winmm.dll", SetLastError = true)]
        internal static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", SetLastError = true)]
        internal static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        internal static bool TrySetTimerResolution(uint microseconds, bool enable, out string? error)
        {
            error = null;
            try
            {
                var status = NtSetTimerResolution(microseconds, enable, out _);
                if (status == 0) return true;
                error = $"NtSetTimerResolution status=0x{status:X}";
                return false;
            }
            catch (DllNotFoundException)
            {
                // Fallback: multimedia timer (1ms granularity best-effort)
                try
                {
                    var code = enable ? timeBeginPeriod(1) : timeEndPeriod(1);
                    if (code == 0) return true;
                    error = $"timeBegin/EndPeriod returned {code}";
                    return false;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
