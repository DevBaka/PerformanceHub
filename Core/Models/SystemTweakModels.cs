using System.Collections.Generic;

namespace DJWinOptimizer.Core.Models
{
    public class SystemTweak
    {
        public string Id { get; set; } = "";
        public string Content { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Panel { get; set; } = "1";
        public bool Checked { get; set; }
        public List<RegistryTweak>? Registry { get; set; }
        public List<ServiceTweak>? Service { get; set; }
        public List<string>? InvokeScript { get; set; }
        public List<string>? UndoScript { get; set; }
        public List<string>? Appx { get; set; }
        public string? Link { get; set; }
    }

    public class RegistryTweak
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Type { get; set; } = "DWord";
        public string? OriginalValue { get; set; }
    }

    public class ServiceTweak
    {
        public string Name { get; set; } = "";
        public string StartupType { get; set; } = "";
        public string? OriginalType { get; set; }
    }

    public class TweakAction
    {
        public string TweakId { get; set; } = "";
        public TweakActionType Action { get; set; }
    }

    public enum TweakActionType
    {
        Apply,
        Undo
    }

    public class TweakResult
    {
        public string TweakId { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
