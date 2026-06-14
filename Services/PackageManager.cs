using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using PerformanceHub.Core.Interfaces;
using PerformanceHub.Core.Models;
using PerformanceHub.Utils;

namespace PerformanceHub.Services
{
    public class PackageManager : IPackageManager
    {
        private readonly ILogger _log;
        private readonly string _applicationsJsonPath;
        private Dictionary<string, PackageApplication>? _applicationsCache;

        public PackageManager(ILogger log)
        {
            _log = log;
            _applicationsJsonPath = Path.Combine(PortablePaths.AppRoot, "config", "applications.json");
            _log.Info($"PackageManager initialized, AppRoot: {PortablePaths.AppRoot}, JSON path: {_applicationsJsonPath}");
            _log.Info($"File exists: {File.Exists(_applicationsJsonPath)}");
            LoadApplications();
        }

        private void LoadApplications()
        {
            try
            {
                _log.Info($"Loading applications from: {_applicationsJsonPath}");
                
                if (!File.Exists(_applicationsJsonPath))
                {
                    _log.Warn($"Applications JSON not found at {_applicationsJsonPath}");
                    _applicationsCache = new Dictionary<string, PackageApplication>();
                    return;
                }

                var json = File.ReadAllText(_applicationsJsonPath);
                _log.Info($"Read {json.Length} characters from JSON file");
                
                // Try to deserialize with case-insensitive options
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                var appsDict = JsonSerializer.Deserialize<Dictionary<string, JsonApplication>>(json, options);

                if (appsDict == null)
                {
                    _log.Warn("Failed to deserialize applications JSON - result is null");
                    _applicationsCache = new Dictionary<string, PackageApplication>();
                    return;
                }

                _log.Info($"Deserialized {appsDict.Count} applications from JSON");

                _applicationsCache = new Dictionary<string, PackageApplication>();
                foreach (var kvp in appsDict)
                {
                    var app = new PackageApplication
                    {
                        Id = kvp.Key,
                        Category = kvp.Value.Category ?? "",
                        Content = kvp.Value.Content ?? "",
                        Description = kvp.Value.Description ?? "",
                        Link = kvp.Value.Link ?? "",
                        Winget = kvp.Value.Winget,
                        Choco = kvp.Value.Choco,
                        Foss = kvp.Value.Foss,
                        Installed = false // Skip installed check for now to speed up loading
                    };
                    _applicationsCache[kvp.Key] = app;
                }

                _log.Info($"Loaded {_applicationsCache.Count} applications from config");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load applications: {ex.Message}", ex);
                _applicationsCache = new Dictionary<string, PackageApplication>();
            }
        }

        private bool CheckInstalled(JsonApplication app)
        {
            // Simple check - could be enhanced with winget list or choco list
            if (!string.IsNullOrEmpty(app.Winget))
            {
                return IsPackageInstalledViaWinget(app.Winget);
            }
            if (!string.IsNullOrEmpty(app.Choco) && app.Choco != "na")
            {
                return IsPackageInstalledViaChoco(app.Choco);
            }
            return false;
        }

        private bool IsPackageInstalledViaWinget(string wingetId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "list --id " + wingetId,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Contains(wingetId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsPackageInstalledViaChoco(string chocoId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "choco",
                    Arguments = "list --local-only --exact " + chocoId,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Contains(chocoId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public IEnumerable<PackageApplication> GetAvailableApplications()
        {
            return _applicationsCache?.Values.OrderBy(a => a.Category).ThenBy(a => a.Content) 
                   ?? Enumerable.Empty<PackageApplication>();
        }

        public bool IsWingetAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool IsChocolateyAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "choco",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public List<PackageManagerResult> ExecuteActions(IEnumerable<PackageManagerAction> actions)
        {
            var results = new List<PackageManagerResult>();

            foreach (var action in actions)
            {
                var result = new PackageManagerResult { PackageId = action.PackageId };

                try
                {
                    if (!_applicationsCache?.ContainsKey(action.PackageId) ?? true)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Package {action.PackageId} not found in applications database";
                        results.Add(result);
                        continue;
                    }

                    var app = _applicationsCache[action.PackageId];
                    var packageManager = DeterminePackageManager(action.Type, app);

                    if (packageManager == PackageManagerType.Winget && !IsWingetAvailable())
                    {
                        result.Success = false;
                        result.ErrorMessage = "Winget is not available";
                        results.Add(result);
                        continue;
                    }

                    if (packageManager == PackageManagerType.Chocolatey && !IsChocolateyAvailable())
                    {
                        result.Success = false;
                        result.ErrorMessage = "Chocolatey is not available";
                        results.Add(result);
                        continue;
                    }

                    if (packageManager == PackageManagerType.Winget)
                    {
                        result.Success = ExecuteWingetAction(app.Winget!, action.Action, out var error);
                        result.ErrorMessage = error;
                    }
                    else if (packageManager == PackageManagerType.Chocolatey)
                    {
                        result.Success = ExecuteChocoAction(app.Choco!, action.Action, out var error);
                        result.ErrorMessage = error;
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = "No suitable package manager available";
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                    _log.Error($"Failed to execute package action for {action.PackageId}", ex);
                }

                results.Add(result);
            }

            return results;
        }

        private PackageManagerType DeterminePackageManager(PackageManagerType requestedType, PackageApplication app)
        {
            if (requestedType != PackageManagerType.Auto)
                return requestedType;

            // Auto: prefer winget, fallback to choco
            if (!string.IsNullOrEmpty(app.Winget) && IsWingetAvailable())
                return PackageManagerType.Winget;

            if (!string.IsNullOrEmpty(app.Choco) && app.Choco != "na" && IsChocolateyAvailable())
                return PackageManagerType.Chocolatey;

            // Fallback to whatever is available
            if (!string.IsNullOrEmpty(app.Winget))
                return PackageManagerType.Winget;

            if (!string.IsNullOrEmpty(app.Choco) && app.Choco != "na")
                return PackageManagerType.Chocolatey;

            return PackageManagerType.Winget; // Default
        }

        private bool ExecuteWingetAction(string packageId, PackageAction action, out string? error)
        {
            error = null;
            try
            {
                var arguments = action == PackageAction.Install
                    ? $"install --id {packageId} --silent --accept-source-agreements --accept-package-agreements --source winget"
                    : $"uninstall --id {packageId} --silent --source winget";

                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Failed to start winget process";
                    return false;
                }

                var output = process.StandardOutput.ReadToEnd();
                var errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // Winget return codes: 0 = success, -1978335189 = no update needed (treat as success for install)
                if (process.ExitCode == 0 || (action == PackageAction.Install && process.ExitCode == -1978335189))
                {
                    _log.Info($"Winget {action} successful for {packageId}");
                    return true;
                }

                error = $"Winget exit code: {process.ExitCode}. Error: {errorOutput}";
                _log.Error($"Winget {action} failed for {packageId}: {error}");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Winget {action} exception for {packageId}", ex);
                return false;
            }
        }

        private bool ExecuteChocoAction(string packageId, PackageAction action, out string? error)
        {
            error = null;
            try
            {
                var arguments = action == PackageAction.Install
                    ? $"install {packageId} -y"
                    : $"uninstall {packageId} -y";

                var psi = new ProcessStartInfo
                {
                    FileName = "choco",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Failed to start choco process";
                    return false;
                }

                var output = process.StandardOutput.ReadToEnd();
                var errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _log.Info($"Chocolatey {action} successful for {packageId}");
                    return true;
                }

                error = $"Chocolatey exit code: {process.ExitCode}. Error: {errorOutput}";
                _log.Error($"Chocolatey {action} failed for {packageId}: {error}");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Chocolatey {action} exception for {packageId}", ex);
                return false;
            }
        }

        public bool InstallChocolatey()
        {
            try
            {
                _log.Info("Installing Chocolatey...");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))\"",
                    UseShellExecute = true,
                    Verb = "runas", // Run as administrator
                    CreateNoWindow = false
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _log.Error("Failed to start Chocolatey installation");
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _log.Info("Chocolatey installed successfully");
                    return true;
                }

                _log.Error($"Chocolatey installation failed with exit code {process.ExitCode}");
                return false;
            }
            catch (Exception ex)
            {
                _log.Error("Chocolatey installation exception", ex);
                return false;
            }
        }

        // JSON mapping class for deserialization
        private class JsonApplication
        {
            public string? Category { get; set; }
            public string? Choco { get; set; }
            public string? Content { get; set; }
            public string? Description { get; set; }
            public string? Link { get; set; }
            public string? Winget { get; set; }
            public bool Foss { get; set; }
        }
    }
}
