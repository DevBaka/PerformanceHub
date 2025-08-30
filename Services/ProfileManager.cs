using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;
using DJWinOptimizer.Utils;

namespace DJWinOptimizer.Services
{
    public class ProfileManager : IProfileManager
    {
        private readonly ILogger _log;
        public Profile? ActiveProfile { get; private set; }

        public ProfileManager(ILogger log)
        {
            _log = log;
            try
            {
                var dir = DJWinOptimizer.Utils.PortablePaths.ProfilesDir;
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                var existingFiles = System.IO.Directory.EnumerateFiles(dir, "*.json").ToList();
                _log.Info($"ProfilesDir: '{dir}', existing profile files: {existingFiles.Count}");
                if (existingFiles.Count == 0)
                {
                    EnsureDefaultProfiles();
                }
            }
            catch (Exception ex)
            {
                _log.Error("ProfileManager init error while checking defaults", ex);
                // Fallback
                EnsureDefaultProfiles();
            }
        }

        public IEnumerable<Profile> GetAll()
        {
            var list = new List<Profile>();
            foreach (var file in Directory.GetFiles(PortablePaths.ProfilesDir, "*.json"))
            {
                var p = JsonUtil.Load<Profile>(file);
                if (p != null) list.Add(p);
            }
            return list.OrderBy(p => p.Name);
        }

        public Profile Create(string name)
        {
            var p = new Profile { Name = name };
            Save(p);
            return p;
        }

        public void Save(Profile profile)
        {
            var path = Path.Combine(PortablePaths.ProfilesDir, Sanitize(profile.Name) + ".json");
            JsonUtil.Save(path, profile);
            _log.Info($"Saved profile '{profile.Name}'.");
        }

        public void Delete(string name)
        {
            var path = Path.Combine(PortablePaths.ProfilesDir, Sanitize(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
            _log.Warn($"Deleted profile '{name}'.");
        }

        public Profile? GetByName(string name)
        {
            var path = Path.Combine(PortablePaths.ProfilesDir, Sanitize(name) + ".json");
            return JsonUtil.Load<Profile>(path);
        }

        public Profile? Import(string filePath)
        {
            try
            {
                var p = JsonUtil.Load<Profile>(filePath);
                if (p == null) return null;
                // Save under its name into Profiles dir
                Save(p);
                _log.Info($"Imported profile from '{filePath}' as '{p.Name}'.");
                return p;
            }
            catch (Exception ex)
            {
                _log.Error($"Import failed for '{filePath}'", ex);
                return null;
            }
        }

        public bool Export(string name, string destinationPath)
        {
            try
            {
                var p = GetByName(name);
                if (p == null) return false;
                var json = System.Text.Json.JsonSerializer.Serialize(p, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(destinationPath, json);
                _log.Info($"Exported profile '{name}' to '{destinationPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Export failed for '{name}' -> '{destinationPath}'", ex);
                return false;
            }
        }

        public bool ApplyProfile(Profile profile)
        {
            try
            {
                // Revert previous if needed
                if (ActiveProfile != null)
                {
                    RevertProfile(ActiveProfile);
                }

                // Preflight checks
                var issues = Core.App.Instance!.Preflight.Run(profile);
                foreach (var i in issues) _log.Warn($"Preflight: {i}");

                // Power plan
                if (!string.IsNullOrWhiteSpace(profile.PowerPlanGuid))
                {
                    if (!Core.App.Instance!.PowerPlans.TrySetActive(profile.PowerPlanGuid, out var planErr))
                    {
                        _log.Warn($"Failed to set power plan to '{profile.PowerPlanGuid}'. {planErr}");
                    }
                }
                else
                {
                    _log.Info("Power plan: No change (profile has no GUID set).");
                }
                // Timer resolution
                try
                {
                    Core.App.Instance!.TimerResolution.SetOneMillisecond(profile.TimerResolution == TimerResolutionMode.OneMs);
                }
                catch { }
                // Services
                Core.App.Instance!.ServiceManager.Apply(profile.Services);
                // Process priorities
                Core.App.Instance!.ProcPriority.Apply(profile.ProcessPriorities);
                // Audio preset (best-effort)
                if (!Core.App.Instance!.Audio.ApplyPreset(profile.Audio, out var audioErr) && !string.IsNullOrWhiteSpace(audioErr))
                    _log.Warn($"Audio apply: {audioErr}");
                // Launch programs
                Core.App.Instance!.Launcher.Launch(profile.Programs?.LaunchOnEnter ?? new());

                ActiveProfile = profile;
                _log.Info($"Applied profile '{profile.Name}'.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to apply profile '{profile.Name}'", ex);
                return false;
            }
        }

        public bool ApplyProfileByName(string name)
        {
            var p = GetByName(name);
            return p != null && ApplyProfile(p);
        }

        private void RevertProfile(Profile profile)
        {
            try
            {
                // Kill programs first
                Core.App.Instance!.Launcher.Kill(profile.Programs?.KillOnExit ?? new());
                Core.App.Instance!.ServiceManager.Revert(profile.Services);
                Core.App.Instance!.ProcPriority.Revert(profile.ProcessPriorities);
                // Restore timer resolution to stock
                try { Core.App.Instance!.TimerResolution.SetOneMillisecond(false); } catch { }
            }
            catch (Exception ex)
            {
                _log.Error("Revert profile error", ex);
            }
        }

        private static string Sanitize(string name)
            => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

        private void EnsureDefaultProfiles()
        {
            try
            {
                var dir = DJWinOptimizer.Utils.PortablePaths.ProfilesDir;
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                // Only create defaults if the directory truly has no .json files
                if (!System.IO.Directory.EnumerateFiles(dir, "*.json").Any())
                {
                    var defaults = DefaultProfiles.Create();
                    foreach (var p in defaults) Save(p);
                    _log.Info("Created default profiles.");
                }
            }
            catch (Exception ex)
            {
                _log.Error("EnsureDefaultProfiles error", ex);
            }
        }
    }

    internal static class DefaultProfiles
    {
        public static IEnumerable<Profile> Create()
        {
            yield return new Profile
            {
                Name = "Idle Mode (Low Power)",
                Description = "Minimum power usage",
                PowerPlanGuid = null,
                Services = new ServiceToggles
                {
                    DisableSearchIndex = true,
                    PauseOneDrive = true
                },
                ProcessPriorities = new(),
                Targets = new()
            };
            yield return new Profile
            {
                Name = "Balanced",
                Description = "Default balanced",
                PowerPlanGuid = null,
                Services = new ServiceToggles(),
                ProcessPriorities = new(),
                Targets = new()
            };
            yield return new Profile
            {
                Name = "High Power Gaming",
                Description = "Gaming performance",
                PowerPlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", // High performance default GUID
                Services = new ServiceToggles
                {
                    DisableSysMain = true,
                    DisableSearchIndex = true,
                    DisableGameDvr = true,
                    PauseWindowsUpdates = true
                },
                ProcessPriorities = new() { ["cs2.exe"] = "High", ["bf2042.exe"] = "High" },
                Targets = new() { "cs2", "bf2042" }
            };
            yield return new Profile
            {
                Name = "High Power Streaming/DJ",
                Description = "Streaming / DJ performance",
                PowerPlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                Services = new ServiceToggles
                {
                    DisableSearchIndex = true,
                    PauseWindowsUpdates = true
                },
                ProcessPriorities = new() { ["obs64.exe"] = "AboveNormal", ["Traktor.exe"] = "High", ["rekordbox.exe"] = "High", ["Serato.exe"] = "High", ["virtualdj.exe"] = "High" },
                Targets = new() { "obs64", "Traktor", "rekordbox", "Serato", "virtualdj" },
                Audio = new AudioOptimizations { EnableWasapiExclusive = true }
            };
        }
    }
}
