using System.ComponentModel;

namespace ProfileDeck.Core.Models;

public sealed class DisplayProfile
{
    public string Name { get; set; } = "Unnamed";
    public string? Description { get; set; }
    public List<DisplayProfileEntry> Displays { get; set; } = new();
}

/// <summary>One physical monitor's desired state within a display profile.</summary>
public sealed class DisplayProfileEntry : INotifyPropertyChanged
{
    /// <summary>Stable hardware identity (CCD target device path, EDID-derived). Never an index.</summary>
    public string HardwareId { get; set; } = "";

    /// <summary>Last known friendly name, stored only for display/debugging/fallback matching.</summary>
    public string? FriendlyNameHint { get; set; }

    public bool Active { get; set; }

    private bool _primary;
    // Raises PropertyChanged so the editor UI can enforce "only one display may be primary"
    // (Windows itself requires exactly one) even when Primary is toggled off programmatically.
    public bool Primary
    {
        get => _primary;
        set
        {
            if (_primary == value) return;
            _primary = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Primary)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double RefreshRateHz { get; set; }

    /// <summary>
    /// Displays sharing the same non-zero Group value are cloned (mirrored) onto one source.
    /// 0 (or a value unique to this entry) means the display gets its own extended source.
    /// </summary>
    public int Group { get; set; }

    /// <summary>Desired DPI scale in percent (100/125/150/.../500). Null = leave the display's current
    /// scale untouched. Windows only accepts a fixed set of steps and caps at what the monitor/GPU reports
    /// as its maximum - a requested value outside that gets rounded/clamped when the profile is applied.</summary>
    public int? DpiScalePercent { get; set; }
}
