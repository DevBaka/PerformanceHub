using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using DJWinOptimizer.Core.Interfaces;
using DJWinOptimizer.Core.Models;
using DJWinOptimizer.Utils;
using Microsoft.Win32;

namespace DJWinOptimizer.Services
{
    public class SystemTweaksManager : ISystemTweaksManager
    {
        private readonly ILogger _log;
        private readonly string _tweaksJsonPath;
        private Dictionary<string, SystemTweak>? _tweaksCache;

        public SystemTweaksManager(ILogger log)
        {
            _log = log;
            _tweaksJsonPath = Path.Combine(PortablePaths.AppRoot, "config", "tweaks.json");
            _log.Info($"SystemTweaksManager initialized, AppRoot: {PortablePaths.AppRoot}, JSON path: {_tweaksJsonPath}");
            _log.Info($"File exists: {File.Exists(_tweaksJsonPath)}");
            LoadTweaks();
        }

        private void LoadTweaks()
        {
            try
            {
                _log.Info($"Loading tweaks from: {_tweaksJsonPath}");
                
                if (!File.Exists(_tweaksJsonPath))
                {
                    _log.Warn($"Tweaks JSON not found at {_tweaksJsonPath}");
                    _tweaksCache = new Dictionary<string, SystemTweak>();
                    return;
                }

                var json = File.ReadAllText(_tweaksJsonPath);
                _log.Info($"Read {json.Length} characters from JSON file");
                
                // Use Newtonsoft.Json which is more tolerant of invalid characters
                var settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                
                var tweaksDict = JsonConvert.DeserializeObject<Dictionary<string, JsonTweak>>(json, settings);

                if (tweaksDict == null)
                {
                    _log.Warn("Failed to deserialize tweaks JSON - result is null");
                    _tweaksCache = new Dictionary<string, SystemTweak>();
                    return;
                }

                _log.Info($"Deserialized {tweaksDict.Count} tweaks from JSON");

                _tweaksCache = new Dictionary<string, SystemTweak>();
                foreach (var kvp in tweaksDict)
                {
                    var tweak = ConvertJsonToTweak(kvp.Key, kvp.Value);
                    _tweaksCache[kvp.Key] = tweak;
                }

                _log.Info($"Loaded {_tweaksCache.Count} system tweaks from config");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load system tweaks: {ex.Message}", ex);
                _tweaksCache = new Dictionary<string, SystemTweak>();
            }
        }

        private SystemTweak ConvertJsonToTweak(string id, JsonTweak jsonTweak)
        {
            return new SystemTweak
            {
                Id = id,
                Content = jsonTweak.Content ?? "",
                Description = jsonTweak.Description ?? "",
                Category = jsonTweak.Category ?? "",
                Panel = jsonTweak.Panel ?? "1",
                Checked = jsonTweak.Checked ?? false,
                Registry = jsonTweak.Registry?.Select(r => new RegistryTweak
                {
                    Path = r.Path ?? "",
                    Name = r.Name ?? "",
                    Value = r.Value ?? "",
                    Type = r.Type ?? "DWord",
                    OriginalValue = r.OriginalValue
                }).ToList(),
                Service = jsonTweak.Service?.Select(s => new ServiceTweak
                {
                    Name = s.Name ?? "",
                    StartupType = s.StartupType ?? "",
                    OriginalType = s.OriginalType
                }).ToList(),
                InvokeScript = jsonTweak.InvokeScript,
                UndoScript = jsonTweak.UndoScript,
                Appx = jsonTweak.Appx,
                Link = jsonTweak.Link
            };
        }

        public IEnumerable<SystemTweak> GetAvailableTweaks()
        {
            return _tweaksCache?.Values.OrderBy(t => t.Category).ThenBy(t => t.Content) 
                   ?? Enumerable.Empty<SystemTweak>();
        }

        public List<TweakResult> ExecuteActions(IEnumerable<TweakAction> actions)
        {
            var results = new List<TweakResult>();

            foreach (var action in actions)
            {
                var result = new TweakResult { TweakId = action.TweakId };

                try
                {
                    if (!_tweaksCache?.ContainsKey(action.TweakId) ?? true)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Tweak {action.TweakId} not found in tweaks database";
                        results.Add(result);
                        continue;
                    }

                    var tweak = _tweaksCache[action.TweakId];

                    if (action.Action == TweakActionType.Apply)
                    {
                        result.Success = ApplyTweak(tweak, out var error);
                        result.ErrorMessage = error;
                    }
                    else
                    {
                        result.Success = UndoTweak(tweak, out var error);
                        result.ErrorMessage = error;
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                    _log.Error($"Failed to execute tweak action for {action.TweakId}", ex);
                }

                results.Add(result);
            }

            return results;
        }

        private bool ApplyTweak(SystemTweak tweak, out string? error)
        {
            error = null;

            try
            {
                // Apply registry changes
                if (tweak.Registry != null && tweak.Registry.Count > 0)
                {
                    foreach (var reg in tweak.Registry)
                    {
                        if (!ApplyRegistryTweak(reg, out var regError))
                        {
                            error = regError; // Pass through the error directly (including ADMIN_REQUIRED prefix)
                            return false;
                        }
                    }
                }

                // Apply service changes
                if (tweak.Service != null && tweak.Service.Count > 0)
                {
                    foreach (var svc in tweak.Service)
                    {
                        if (!ApplyServiceTweak(svc, out var svcError))
                        {
                            error = $"Service tweak failed: {svcError}";
                            return false;
                        }
                    }
                }

                // Execute invoke script
                if (tweak.InvokeScript != null && tweak.InvokeScript.Count > 0)
                {
                    if (!ExecutePowerShellScript(tweak.InvokeScript, out var scriptError))
                    {
                        error = $"Invoke script failed: {scriptError}";
                        return false;
                    }
                }

                // Remove AppX packages
                if (tweak.Appx != null && tweak.Appx.Count > 0)
                {
                    foreach (var appx in tweak.Appx)
                    {
                        if (!RemoveAppxPackage(appx, out var appxError))
                        {
                            error = $"AppX removal failed: {appxError}";
                            return false;
                        }
                    }
                }

                _log.Info($"Successfully applied tweak: {tweak.Id}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to apply tweak {tweak.Id}", ex);
                return false;
            }
        }

        private bool UndoTweak(SystemTweak tweak, out string? error)
        {
            error = null;

            try
            {
                // Undo registry changes
                if (tweak.Registry != null && tweak.Registry.Count > 0)
                {
                    foreach (var reg in tweak.Registry)
                    {
                        if (!UndoRegistryTweak(reg, out var regError))
                        {
                            error = $"Registry undo failed: {regError}";
                            return false;
                        }
                    }
                }

                // Undo service changes
                if (tweak.Service != null && tweak.Service.Count > 0)
                {
                    foreach (var svc in tweak.Service)
                    {
                        if (!UndoServiceTweak(svc, out var svcError))
                        {
                            error = $"Service undo failed: {svcError}";
                            return false;
                        }
                    }
                }

                // Execute undo script
                if (tweak.UndoScript != null && tweak.UndoScript.Count > 0)
                {
                    if (!ExecutePowerShellScript(tweak.UndoScript, out var scriptError))
                    {
                        error = $"Undo script failed: {scriptError}";
                        return false;
                    }
                }

                _log.Info($"Successfully undid tweak: {tweak.Id}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to undo tweak {tweak.Id}", ex);
                return false;
            }
        }

        private bool ApplyRegistryTweak(RegistryTweak tweak, out string? error)
        {
            error = null;
            try
            {
                var hive = GetRegistryHive(tweak.Path);
                if (hive == null)
                {
                    error = $"Invalid registry path: {tweak.Path}";
                    return false;
                }

                var subKeyPath = tweak.Path.Substring(tweak.Path.IndexOf('\\') + 1);
                using var key = hive.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree);

                if (key == null)
                {
                    error = $"Failed to open registry key: {tweak.Path}";
                    return false;
                }

                var value = ParseRegistryValue(tweak.Value, tweak.Type);
                key.SetValue(tweak.Name, value, GetRegistryValueType(tweak.Type));

                _log.Info($"Applied registry tweak: {tweak.Path}\\{tweak.Name} = {tweak.Value}");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = "ADMIN_REQUIRED:" + ex.Message;
                _log.Error($"Failed to apply registry tweak (admin required): {tweak.Path}\\{tweak.Name}", ex);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to apply registry tweak: {tweak.Path}\\{tweak.Name}", ex);
                return false;
            }
        }

        private bool UndoRegistryTweak(RegistryTweak tweak, out string? error)
        {
            error = null;
            try
            {
                var hive = GetRegistryHive(tweak.Path);
                if (hive == null)
                {
                    error = $"Invalid registry path: {tweak.Path}";
                    return false;
                }

                var subKeyPath = tweak.Path.Substring(tweak.Path.IndexOf('\\') + 1);
                using var key = hive.OpenSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree);

                if (key == null)
                {
                    // Key doesn't exist, nothing to undo
                    return true;
                }

                if (tweak.OriginalValue == "<RemoveEntry>")
                {
                    key.DeleteValue(tweak.Name, false);
                    _log.Info($"Removed registry value: {tweak.Path}\\{tweak.Name}");
                }
                else if (!string.IsNullOrEmpty(tweak.OriginalValue))
                {
                    var value = ParseRegistryValue(tweak.OriginalValue, tweak.Type);
                    key.SetValue(tweak.Name, value, GetRegistryValueType(tweak.Type));
                    _log.Info($"Restored registry value: {tweak.Path}\\{tweak.Name} = {tweak.OriginalValue}");
                }
                else
                {
                    // No original value specified, leave as is
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to undo registry tweak: {tweak.Path}\\{tweak.Name}", ex);
                return false;
            }
        }

        private bool ApplyServiceTweak(ServiceTweak tweak, out string? error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config {tweak.Name} start= {tweak.StartupType}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Failed to start sc.exe process";
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _log.Info($"Applied service tweak: {tweak.Name} -> {tweak.StartupType}");
                    return true;
                }

                error = $"sc.exe exit code: {process.ExitCode}";
                _log.Error($"Failed to apply service tweak: {tweak.Name}");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to apply service tweak: {tweak.Name}", ex);
                return false;
            }
        }

        private bool UndoServiceTweak(ServiceTweak tweak, out string? error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(tweak.OriginalType))
                {
                    // No original type specified, skip
                    return true;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"config {tweak.Name} start= {tweak.OriginalType}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Failed to start sc.exe process";
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _log.Info($"Restored service: {tweak.Name} -> {tweak.OriginalType}");
                    return true;
                }

                error = $"sc.exe exit code: {process.ExitCode}";
                _log.Error($"Failed to restore service: {tweak.Name}");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to restore service: {tweak.Name}", ex);
                return false;
            }
        }

        private bool ExecutePowerShellScript(List<string> scriptLines, out string? error)
        {
            error = null;
            try
            {
                var script = string.Join("\n", scriptLines);
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command -",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Failed to start PowerShell process";
                    return false;
                }

                process.StandardInput.Write(script);
                process.StandardInput.Close();

                var output = process.StandardOutput.ReadToEnd();
                var errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _log.Info($"PowerShell script executed successfully");
                    return true;
                }

                error = $"PowerShell exit code: {process.ExitCode}. Error: {errorOutput}";
                _log.Error($"PowerShell script failed: {error}");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error("Failed to execute PowerShell script", ex);
                return false;
            }
        }

        private bool RemoveAppxPackage(string packageName, out string? error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage {packageName} -AllUsers | Remove-AppxPackage -AllUsers\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "Failed to start PowerShell process";
                    return false;
                }

                var output = process.StandardOutput.ReadToEnd();
                var errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                _log.Info($"Attempted to remove AppX package: {packageName}");
                return true; // Even if package doesn't exist, we consider it successful
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _log.Error($"Failed to remove AppX package: {packageName}", ex);
                return false;
            }
        }

        public bool IsTweakApplied(string tweakId)
        {
            try
            {
                if (!_tweaksCache?.ContainsKey(tweakId) ?? true)
                    return false;

                var tweak = _tweaksCache[tweakId];

                // Check registry tweaks
                if (tweak.Registry != null && tweak.Registry.Count > 0)
                {
                    foreach (var reg in tweak.Registry)
                    {
                        if (!IsRegistryTweakApplied(reg))
                            return false;
                    }
                }

                // Check service tweaks
                if (tweak.Service != null && tweak.Service.Count > 0)
                {
                    foreach (var svc in tweak.Service)
                    {
                        if (!IsServiceTweakApplied(svc))
                            return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRegistryTweakApplied(RegistryTweak tweak)
        {
            try
            {
                var hive = GetRegistryHive(tweak.Path);
                if (hive == null) return false;

                var subKeyPath = tweak.Path.Substring(tweak.Path.IndexOf('\\') + 1);
                using var key = hive.OpenSubKey(subKeyPath);

                if (key == null) return false;

                var currentValue = key.GetValue(tweak.Name);
                var expectedValue = ParseRegistryValue(tweak.Value, tweak.Type);

                return Equals(currentValue, expectedValue);
            }
            catch
            {
                return false;
            }
        }

        private bool IsServiceTweakApplied(ServiceTweak tweak)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = $"query {tweak.Name}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Contains(tweak.StartupType, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private RegistryKey? GetRegistryHive(string path)
        {
            if (path.StartsWith("HKLM:\\")) return Registry.LocalMachine;
            if (path.StartsWith("HKCU:\\")) return Registry.CurrentUser;
            if (path.StartsWith("HKCR:\\")) return Registry.ClassesRoot;
            return null;
        }

        private object ParseRegistryValue(string value, string type)
        {
            return type.ToUpper() switch
            {
                "DWORD" => int.Parse(value),
                "QWORD" => long.Parse(value),
                "STRING" => value,
                "EXPANDSTRING" => value,
                "MULTISTRING" => value.Split('\n'),
                "BINARY" => Convert.FromHexString(value.Replace(" ", "")),
                _ => value
            };
        }

        private RegistryValueKind GetRegistryValueType(string type)
        {
            return type.ToUpper() switch
            {
                "DWORD" => RegistryValueKind.DWord,
                "QWORD" => RegistryValueKind.QWord,
                "STRING" => RegistryValueKind.String,
                "EXPANDSTRING" => RegistryValueKind.ExpandString,
                "MULTISTRING" => RegistryValueKind.MultiString,
                "BINARY" => RegistryValueKind.Binary,
                _ => RegistryValueKind.String
            };
        }

        // JSON mapping classes
        private class JsonTweak
        {
            public string? Content { get; set; }
            public string? Description { get; set; }
            public string? Category { get; set; }
            public string? Panel { get; set; }
            public bool? Checked { get; set; }
            public List<JsonRegistryTweak>? Registry { get; set; }
            public List<JsonServiceTweak>? Service { get; set; }
            public List<string>? InvokeScript { get; set; }
            public List<string>? UndoScript { get; set; }
            public List<string>? Appx { get; set; }
            public string? Link { get; set; }
        }

        private class JsonRegistryTweak
        {
            public string? Path { get; set; }
            public string? Name { get; set; }
            public string? Value { get; set; }
            public string? Type { get; set; }
            public string? OriginalValue { get; set; }
        }

        private class JsonServiceTweak
        {
            public string? Name { get; set; }
            public string? StartupType { get; set; }
            public string? OriginalType { get; set; }
        }
    }
}
