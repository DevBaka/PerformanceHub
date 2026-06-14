using System.Collections.Generic;

namespace PerformanceHub.Core.Models
{
    public class PackageApplication
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Content { get; set; } = "";
        public string Description { get; set; } = "";
        public string Link { get; set; } = "";
        public string? Winget { get; set; }
        public string? Choco { get; set; }
        public bool Foss { get; set; }
        public bool Installed { get; set; }
    }

    public class PackageManagerAction
    {
        public string PackageId { get; set; } = "";
        public PackageManagerType Type { get; set; }
        public PackageAction Action { get; set; }
    }

    public enum PackageManagerType
    {
        Winget,
        Chocolatey,
        Auto
    }

    public enum PackageAction
    {
        Install,
        Uninstall
    }

    public class PackageManagerResult
    {
        public string PackageId { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
