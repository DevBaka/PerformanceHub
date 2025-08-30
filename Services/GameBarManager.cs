using DJWinOptimizer.Core.Interfaces;
using System.Linq;
using DJWinOptimizer.Utils;
using Microsoft.Win32;

namespace DJWinOptimizer.Services
{
    public class GameBarManager : IGameBarManager
    {
        private readonly ILogger _log;
        public GameBarManager(ILogger log) { _log = log; }

        public bool SetEnabled(bool enabled, out string? error)
        {
            // HKCU toggles for Game Bar experience
            // Not all keys exist on all versions; treat missing as success once set.
            error = null;
            var ok1 = RegistryUtil.TrySetDword(RegistryHive.CurrentUser, "SOFTWARE/Microsoft/GameBar", "Enabled", enabled ? 1 : 0, out var e1);
            var ok2 = RegistryUtil.TrySetDword(RegistryHive.CurrentUser, "SOFTWARE/Microsoft/GameBar", "AutoGameModeEnabled", enabled ? 1 : 0, out var e2);
            if (!(ok1 && ok2))
            {
                error = string.Join(" | ", new[] { e1, e2 }.Where(s => !string.IsNullOrWhiteSpace(s))!);
                _log.Warn($"Game Bar set {(enabled ? "enable" : "disable")} partial failure: {error}");
                return false;
            }
            _log.Info($"Game Bar {(enabled ? "enabled" : "disabled")}");
            return true;
        }
    }
}
