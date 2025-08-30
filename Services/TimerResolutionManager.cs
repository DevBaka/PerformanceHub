using System;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Utils;

namespace DJWinOptimizer.Services
{
    public class TimerResolutionManager : ITimerResolutionManager
    {
        private readonly ILogger _log;
        private bool _oneMs;
        public bool IsOneMillisecond => _oneMs;

        public TimerResolutionManager(ILogger log) { _log = log; }

        public void SetOneMillisecond(bool enable)
        {
            if (_oneMs == enable) return;
            if (Win32Native.TrySetTimerResolution(10000 /*1ms in 100ns*/, enable, out var err))
            {
                _oneMs = enable;
                _log.Info($"Timer resolution set to {(enable ? "1ms" : "stock")}");
            }
            else
            {
                _log.Warn($"Failed to set timer resolution: {err}");
            }
        }

        public void Dispose()
        {
            if (_oneMs)
            {
                try { SetOneMillisecond(false); } catch { }
            }
        }
    }
}
