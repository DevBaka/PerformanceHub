using System.Collections.Generic;
using PerformanceHub.Core.Interfaces;
using PerformanceHub.Core.Models;
using PerformanceHub.Utils;

namespace PerformanceHub.Services
{
    public class PreFlightChecker : IPreFlightChecker
    {
        private readonly ILogger _log;
        public PreFlightChecker(ILogger log) { _log = log; }

        public IReadOnlyList<string> Run(Profile profile)
        {
            var issues = new List<string>();
            // Admin checks for HKLM and schtasks
            if (!AdminUtil.IsAdministrator())
            {
                if (profile.Services.PauseWindowsUpdates || profile.Services.DisableDefenderRealtime || profile.Services.BlockScheduledScans)
                    issues.Add("Some service/security toggles may require Administrator rights.");
            }
            // Power plan GUID format
            if (!string.IsNullOrWhiteSpace(profile.PowerPlanGuid) && !System.Guid.TryParse(profile.PowerPlanGuid, out _))
                issues.Add("PowerPlan GUID is invalid.");
            return issues;
        }
    }
}
