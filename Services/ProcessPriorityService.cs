using System;
using System.Collections.Generic;
using System.Diagnostics;
using DJWinOptimizer.Core.Interfaces;

namespace DJWinOptimizer.Services
{
    public class ProcessPriorityService : IProcessPriorityService
    {
        private readonly ILogger _log;
        public ProcessPriorityService(ILogger log) { _log = log; }

        public void Apply(Dictionary<string, string>? exeToPriority)
        {
            if (exeToPriority == null) return;
            foreach (var kv in exeToPriority)
            {
                TrySet(kv.Key, kv.Value);
            }
        }

        public void Revert(Dictionary<string, string>? exeToPriority)
        {
            if (exeToPriority == null) return;
            foreach (var kv in exeToPriority)
            {
                TrySet(kv.Key, "Normal");
            }
        }

        private void TrySet(string exeName, string priority)
        {
            try
            {
                var procName = System.IO.Path.GetFileNameWithoutExtension(exeName);
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    var desired = priority switch
                    {
                        "Realtime" => ProcessPriorityClass.RealTime,
                        "High" => ProcessPriorityClass.High,
                        "AboveNormal" => ProcessPriorityClass.AboveNormal,
                        "BelowNormal" => ProcessPriorityClass.BelowNormal,
                        "Idle" => ProcessPriorityClass.Idle,
                        _ => ProcessPriorityClass.Normal
                    };

                    bool ok = false;
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        try
                        {
                            if (p.HasExited) break;
                            p.PriorityClass = desired;
                            // Verify
                            if (!p.HasExited && p.PriorityClass == desired)
                            {
                                _log.Info($"Set priority {desired} for {p.ProcessName} (PID {p.Id}) (attempt {attempt}).");
                                ok = true;
                                break;
                            }
                        }
                        catch (Exception exAttempt)
                        {
                            _log.Warn($"Priority set attempt {attempt} for {p.ProcessName} (PID {p.Id}) failed: {exAttempt.Message}");
                        }
                        System.Threading.Thread.Sleep(150);
                    }
                    if (!ok)
                    {
                        _log.Warn($"Failed to set priority {desired} for {p.ProcessName} (PID {p.Id}) after retries.");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to set priority for {exeName}: {ex.Message}");
            }
        }
    }
}
