using System.Text;

namespace Deskrig.Core.Display;

/// <summary>
/// Shared EDID plumbing for every Linux display backend (xrandr, kscreen-doctor, ...): reading the raw
/// blob straight from the kernel via DRM/KMS sysfs (<c>/sys/class/drm/cardN-&lt;port&gt;/edid</c>) - the
/// same source real desktop environments (KDE, GNOME) read monitor identity from, and the only one that's
/// reliable regardless of session type or which display-management tool a given backend shells out to -
/// plus a minimal EDID base-block parser (manufacturer/product/serial and, if present, the monitor name
/// descriptor). Not a full EDID implementation.
/// </summary>
internal static class EdidUtil
{
    /// <summary>Maps connector/port name (e.g. "DP-1", "HDMI-A-2") -> raw EDID hex blob, for every
    /// connector the kernel currently has EDID for.</summary>
    public static Dictionary<string, string> ReadFromSysfs()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/class/drm"))
            {
                // Connector directories are named "card<N>-<port>" (e.g. "card1-HDMI-A-1"); "card1" itself
                // and "renderD*" nodes have no dash and are skipped.
                var name = Path.GetFileName(dir);
                var dash = name.IndexOf('-');
                if (dash < 0) continue;
                var port = name[(dash + 1)..];

                try
                {
                    // stat()'ing this file always reports length 0 (it's a sysfs binary attribute, not a
                    // real file) - the only way to know if there's real EDID behind it is to just read it.
                    var bytes = File.ReadAllBytes(Path.Combine(dir, "edid"));
                    if (bytes.Length >= 128) result[port] = Convert.ToHexString(bytes);
                }
                catch { /* no monitor on this connector, or no read permission - keep going */ }
            }
        }
        catch { /* /sys/class/drm doesn't exist (container, non-DRM setup, ...) */ }

        return result;
    }

    public static string ResolveHardwareId(string port, Dictionary<string, string> edidByPort)
    {
        if (edidByPort.TryGetValue(port, out var hex) && TryParse(hex, out var mfg, out var product, out var serial, out _))
            return $"EDID-{mfg}-{product:X4}-{serial:X8}";
        return $"PORT-{port}";
    }

    /// <summary>
    /// Never falls back to the port name (that's what confusingly showed up as "the monitor" before -
    /// the GPU output, not the actual display) - the port is carried separately via
    /// <see cref="DisplayInfo.ConnectorName"/> instead. Format matches what KDE's own display settings
    /// show for the same EDID data: "&lt;3-letter PNP vendor id&gt; &lt;product name&gt;" (e.g.
    /// "BNQ BenQ LCD", "AUS VG259QMR5A") - deliberately the raw PNP id rather than a guessed-at resolved
    /// company name (e.g. "ASUSTeK COMPUTER INC"), since that needs a vendor-id database we don't ship and
    /// getting it wrong is worse than the plain code. Falls back to the product's hex id if the EDID has no
    /// name descriptor (common on cheaper panels/TVs), or an explicit "unknown" label if there's no EDID at
    /// all (typical only for virtual/nested display servers - VNC, some container setups - real hardware
    /// behind a real DRM driver always has one).
    /// </summary>
    public static string ResolveFriendlyName(string port, Dictionary<string, string> edidByPort)
    {
        if (edidByPort.TryGetValue(port, out var hex) && TryParse(hex, out var mfg, out var product, out _, out var name))
            return string.IsNullOrWhiteSpace(name) ? $"{mfg} {product:X4}" : $"{mfg} {name}";
        return $"Unbekanntes Display ({port})";
    }

    public static bool TryParse(string hex, out string manufacturer, out int product, out uint serial, out string? monitorName)
    {
        manufacturer = ""; product = 0; serial = 0; monitorName = null;
        try
        {
            var bytes = Convert.FromHexString(hex);
            if (bytes.Length < 128) return false;

            // Bytes 8-9: manufacturer id, 3 packed 5-bit letters (bit 15 always 0).
            int mfgWord = (bytes[8] << 8) | bytes[9];
            char c1 = (char)('A' + ((mfgWord >> 10) & 0x1F) - 1);
            char c2 = (char)('A' + ((mfgWord >> 5) & 0x1F) - 1);
            char c3 = (char)('A' + (mfgWord & 0x1F) - 1);
            manufacturer = $"{c1}{c2}{c3}";

            product = bytes[10] | (bytes[11] << 8);
            serial = (uint)(bytes[12] | (bytes[13] << 8) | (bytes[14] << 16) | (bytes[15] << 24));

            for (int d = 0; d < 4; d++)
            {
                int off = 54 + d * 18;
                if (off + 18 > bytes.Length) break;
                // Descriptor: 0x0000, 0x00, tag, 0x00, then up to 13 ASCII chars. Tag 0xFC = monitor name.
                if (bytes[off] == 0 && bytes[off + 1] == 0 && bytes[off + 2] == 0 && bytes[off + 3] == 0xFC)
                {
                    var raw = Encoding.ASCII.GetString(bytes, off + 5, 13);
                    var cut = raw.IndexOf('\n');
                    monitorName = (cut >= 0 ? raw[..cut] : raw).Trim();
                }
            }
            return true;
        }
        catch { return false; }
    }
}
