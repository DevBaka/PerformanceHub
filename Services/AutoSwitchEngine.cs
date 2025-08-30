using System;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;

namespace DJWinOptimizer.Services
{
    public class AutoSwitchEngine : IAutoSwitchEngine, IDisposable
    {
        private readonly ILogger _log;
        private readonly IProfileManager _profiles;
        private readonly IProcessPriorityService _prio;
        private readonly IPowerPlanService _power;
        private readonly System.Timers.Timer _timer;
        public bool Running { get; private set; }
        public string? LastTrigger { get; private set; }

        public AutoSwitchEngine(ILogger log, IProfileManager profiles, IProcessPriorityService prio, IPowerPlanService power)
        {
            _log = log; _profiles = profiles; _prio = prio; _power = power;
            _timer = new System.Timers.Timer(3000); // every 3s
            _timer.Elapsed += Timer_Elapsed;
        }

        public void Start() { if (Running) return; Running = true; _timer.Start(); _log.Info("AutoSwitch started"); }
        public void Stop() { if (!Running) return; Running = false; _timer.Stop(); _log.Info("AutoSwitch stopped"); }

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (!Running) return;
            try
            {
                var procs = Process.GetProcesses()
                                   .Select(p => p.ProcessName)
                                   .Select(n => System.IO.Path.GetFileNameWithoutExtension(n))
                                   .ToHashSet(StringComparer.OrdinalIgnoreCase);

                bool Matches(DJWinOptimizer.Core.Models.Profile p)
                {
                    // Normalize helpers
                    bool HasAny(IEnumerable<string>? list)
                        => list != null && list.Select(x => System.IO.Path.GetFileNameWithoutExtension(x))
                                               .Any(x => procs.Contains(x));
                    bool HasAll(IEnumerable<string>? list)
                        => list != null && list.Select(x => System.IO.Path.GetFileNameWithoutExtension(x))
                                               .All(x => procs.Contains(x));

                    var hasAny = p.TargetsAny != null ? HasAny(p.TargetsAny) : (p.Targets != null && p.Targets.Count > 0 ? HasAny(p.Targets) : true);
                    var hasAll = p.TargetsAll != null ? HasAll(p.TargetsAll) : true;
                    // If both groups are present, require both. If legacy only, hasAny reflects OR on Targets.
                    return hasAny && hasAll;
                }

                int Specificity(DJWinOptimizer.Core.Models.Profile p)
                {
                    int a = p.TargetsAll?.Count ?? 0;
                    int o = p.TargetsAny?.Count ?? 0;
                    int legacy = (p.Targets != null && (p.TargetsAny == null && p.TargetsAll == null)) ? p.Targets.Count : 0;
                    return a * 10 + o + legacy; // weight AND higher than OR; legacy minimal
                }

                var candidates = _profiles.GetAll().Where(Matches).ToList();
                if (candidates.Count > 0)
                {
                    var match = candidates
                        .OrderByDescending(p => p.Priority)
                        .ThenByDescending(p => Specificity(p))
                        .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .First();

                    if (_profiles.ActiveProfile?.Name != match.Name)
                    {
                        _profiles.ApplyProfile(match);
                        LastTrigger = match.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("AutoSwitch tick error", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                _timer.Stop();
                _timer.Elapsed -= Timer_Elapsed;
                _timer.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error("AutoSwitch dispose error", ex);
            }
        }
    }
}
