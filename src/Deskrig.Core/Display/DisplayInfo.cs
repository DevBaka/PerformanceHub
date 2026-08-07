namespace Deskrig.Core.Display;

/// <summary>A physical monitor as currently reported by Windows (CCD API), independent of any profile.</summary>
public sealed class DisplayInfo
{
    public required string HardwareId { get; init; }
    public required string FriendlyName { get; init; }
    public bool IsActive { get; init; }
    public bool IsPrimary { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double RefreshRateHz { get; init; }

    /// <summary>Current, Windows-recommended, and maximum-supported DPI scale for this display's source, in
    /// percent (100/125/150/.../500 - Windows only allows a fixed set of steps, not arbitrary values).
    /// 0 if unavailable (e.g. inactive display with no source assigned).</summary>
    public int CurrentDpiScalePercent { get; init; }
    public int RecommendedDpiScalePercent { get; init; }
    public int MaximumDpiScalePercent { get; init; }

    /// <summary>Displays that currently share this key are cloned onto the same source.</summary>
    public string CloneGroupKey { get; init; } = "";

    /// <summary>The physical port/output this display is wired to (e.g. "DP-1", "HDMI-A-2" on Linux) -
    /// informational only, never used as the display's identity (that's <see cref="HardwareId"/>) or as a
    /// stand-in for its name when the real one isn't known. Null where the platform backend doesn't track
    /// a separate port label (currently: Windows).</summary>
    public string? ConnectorName { get; init; }

    public override string ToString()
    {
        var status = IsActive ? $"{Width}x{Height}@{RefreshRateHz:0.##}Hz @({PositionX},{PositionY})" : "inactive";
        var line = $"{FriendlyName} [{HardwareId}] {status}{(IsPrimary ? " PRIMARY" : "")}";
        return string.IsNullOrEmpty(ConnectorName) ? line : $"{line}\n      GPU-Anschluss: {ConnectorName}";
    }
}
