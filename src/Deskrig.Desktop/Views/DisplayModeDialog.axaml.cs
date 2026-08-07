using Avalonia.Controls;
using Avalonia.Interactivity;
using Deskrig.Core.Display;
using Deskrig.Core.Models;
using Deskrig.Desktop.Infrastructure;

namespace Deskrig.Desktop.Views;

public partial class DisplayModeDialog : Window
{
    private static readonly (string Label, double Ratio)[] KnownRatios =
    {
        ("16:9", 16.0 / 9), ("16:10", 16.0 / 10), ("21:9", 21.0 / 9), ("4:3", 4.0 / 3), ("5:4", 5.0 / 4),
    };

    private static readonly int[] ScalingSteps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    private readonly List<(int Width, int Height, int RefreshHz)> _allModes;
    private bool _suppressEvents;

    public int? ResultWidth { get; private set; }
    public int? ResultHeight { get; private set; }
    public double? ResultRefreshHz { get; private set; }
    public int? ResultDpiScalePercent { get; private set; }

    public DisplayModeDialog(DisplayManager displayManager, DisplayProfileEntry entry)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        MonitorNameText.Text = entry.FriendlyNameHint ?? entry.HardwareId;
        _allModes = displayManager.GetPossibleModes(entry.HardwareId).ToList();

        var maxDpi = displayManager.GetMaxDpiScalePercent(entry.HardwareId);
        var dpiHint = maxDpi.HasValue ? $"Maximale Skalierung dieses Displays: {maxDpi}%." : "Maximale Skalierung erst bekannt, sobald das Display aktiv ist.";
        HintText.Text = _allModes.Count == 0
            ? "Keine unterstützten Modi ermittelbar (Monitor evtl. nicht angeschlossen) - Werte können trotzdem frei eingetragen werden."
            : dpiHint + " Hinweis: manche Treiber melden Bildwiederholraten mit Kommawerten (z.B. 119.88 statt 120 Hz) - diese Liste rundet auf ganze Zahlen. " +
              "Für exakte Kommawerte: einmal direkt im System einstellen, dann hier 'Aktuelle Anordnung laden' nutzen.";

        BuildRatioOptions();
        BuildScalingOptions(maxDpi);

        // Pre-select the ratio matching the entry's current resolution, then the resolution/refresh/scale itself.
        var currentRatioLabel = entry.Width > 0 && entry.Height > 0 ? RatioLabel(entry.Width, entry.Height) : "Alle";
        RatioCombo.SelectedItem = RatioCombo.Items.Cast<string>().FirstOrDefault(r => r == currentRatioLabel) ?? RatioCombo.Items[0];
        BuildResolutionOptions();
        SetEditableComboText(ResolutionCombo, entry.Width > 0 ? $"{entry.Width}x{entry.Height}" : "");
        BuildRefreshOptions(entry.Width, entry.Height);
        SetEditableComboText(RefreshCombo, entry.RefreshRateHz > 0 ? FormatHz(entry.RefreshRateHz) : "");
        SetEditableComboText(ScalingCombo, entry.DpiScalePercent?.ToString() ?? "");
    }

    private void BuildRatioOptions()
    {
        var ratios = _allModes
            .Select(m => RatioLabel(m.Width, m.Height))
            .Distinct()
            .OrderBy(r => r)
            .ToList();
        RatioCombo.Items.Clear();
        RatioCombo.Items.Add("Alle");
        foreach (var r in ratios) RatioCombo.Items.Add(r);
        if (RatioCombo.Items.Count > 0) RatioCombo.SelectedIndex = 0;
    }

    private void BuildResolutionOptions()
    {
        var ratioFilter = RatioCombo.SelectedItem as string;
        var resolutions = _allModes
            .Where(m => ratioFilter == "Alle" || ratioFilter == null || RatioLabel(m.Width, m.Height) == ratioFilter)
            .Select(m => (m.Width, m.Height))
            .Distinct()
            .OrderByDescending(r => r.Width * r.Height)
            .Select(r => $"{r.Width}x{r.Height}")
            .ToList();

        _suppressEvents = true;
        var previous = ResolutionCombo.Text;
        ResolutionCombo.Items.Clear();
        foreach (var r in resolutions) ResolutionCombo.Items.Add(r);
        ResolutionCombo.Text = previous;
        _suppressEvents = false;
    }

    private void BuildRefreshOptions(int width, int height)
    {
        var rates = _allModes
            .Where(m => m.Width == width && m.Height == height)
            .Select(m => m.RefreshHz)
            .Distinct()
            .OrderByDescending(r => r)
            .Select(r => FormatHz(r))
            .ToList();

        _suppressEvents = true;
        var previous = RefreshCombo.Text;
        RefreshCombo.Items.Clear();
        foreach (var r in rates) RefreshCombo.Items.Add(r);
        RefreshCombo.Text = !rates.Contains(previous) ? rates.FirstOrDefault() ?? previous : previous;
        _suppressEvents = false;
    }

    private void BuildScalingOptions(int? maxDpi)
    {
        ScalingCombo.Items.Clear();
        foreach (var s in ScalingSteps.Where(s => !maxDpi.HasValue || s <= maxDpi.Value))
            ScalingCombo.Items.Add(s.ToString());
    }

    private void RatioCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        BuildResolutionOptions();
    }

    private void ResolutionCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (TryParseResolution(ResolutionCombo.Text, out var w, out var h))
            BuildRefreshOptions(w, h);
    }

    private async void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ResolutionCombo.Text))
        {
            if (!TryParseResolution(ResolutionCombo.Text, out var w, out var h))
            {
                await Dialogs.WarnAsync(this, "Auflösung bitte im Format BREITExHÖHE angeben, z.B. 1920x1080.");
                return;
            }
            if (!await ConfirmIfCustomAsync("Auflösung", ResolutionCombo.Text, ResolutionCombo.Items)) return;
            ResultWidth = w;
            ResultHeight = h;
        }

        if (!string.IsNullOrWhiteSpace(RefreshCombo.Text))
        {
            if (!double.TryParse(RefreshCombo.Text.Replace("Hz", "").Trim(), out var hz))
            {
                await Dialogs.WarnAsync(this, "Bildwiederholrate bitte als Zahl angeben, z.B. 60 oder 144.");
                return;
            }
            if (!await ConfirmIfCustomAsync("Bildwiederholrate", RefreshCombo.Text, RefreshCombo.Items)) return;
            ResultRefreshHz = hz;
        }

        if (!string.IsNullOrWhiteSpace(ScalingCombo.Text))
        {
            if (!int.TryParse(ScalingCombo.Text.Replace("%", "").Trim(), out var dpi))
            {
                await Dialogs.WarnAsync(this, "Skalierung bitte als Prozentzahl angeben, z.B. 150.");
                return;
            }
            if (!await ConfirmIfCustomAsync("Skalierung", ScalingCombo.Text, ScalingCombo.Items)) return;
            ResultDpiScalePercent = dpi;
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>
    /// If the typed text doesn't match one of the dropdown's known-good values exactly, this is a value the
    /// driver never advertised as supported - warn and require an explicit OK before proceeding, since
    /// applying it can silently fall back to a completely different mode with no further feedback once the
    /// profile is actually applied.
    /// </summary>
    private async Task<bool> ConfirmIfCustomAsync(string fieldLabel, string enteredText, System.Collections.IEnumerable knownItems)
    {
        var normalized = enteredText.Trim();
        bool isKnown = knownItems.Cast<object>().Any(i => string.Equals(i.ToString(), normalized, StringComparison.OrdinalIgnoreCase));
        if (isKnown) return true;

        return await Dialogs.ConfirmOkCancelAsync(this,
            $"'{normalized}' steht nicht in der Liste der von diesem Display unterstützten Werte für {fieldLabel}.\n\n" +
            "Der Treiber könnte diesen Wert ablehnen - dann wird beim Anwenden automatisch der Standardmodus des Monitors verwendet, ohne weitere Rückfrage.\n\n" +
            "Trotzdem übernehmen?",
            "Nicht unterstützter Wert");
    }

    private static bool TryParseResolution(string? text, out int width, out int height)
    {
        width = height = 0;
        var parts = text?.Split('x', 'X', '×') ?? Array.Empty<string>();
        return parts.Length == 2 && int.TryParse(parts[0].Trim(), out width) && int.TryParse(parts[1].Trim(), out height) && width > 0 && height > 0;
    }

    private static string FormatHz(double hz) => hz % 1 == 0 ? $"{hz:0}" : $"{hz:0.##}";

    private static void SetEditableComboText(ComboBox combo, string text) => combo.Text = text;

    private static string RatioLabel(int w, int h)
    {
        if (w <= 0 || h <= 0) return "Alle";
        double r = (double)w / h;
        var best = KnownRatios.OrderBy(k => Math.Abs(k.Ratio - r)).First();
        return Math.Abs(best.Ratio - r) < 0.02 ? best.Label : $"{w}:{h}";
    }
}
