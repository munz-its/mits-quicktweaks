namespace MunzQuickTweaks.Core.Tools;

public sealed record DriverInfo(string Device, string DeviceClass, string Manufacturer, string Version, DateTime? Date, bool IsSigned);

/// <summary>Bewertung und Herstellerseiten für installierte Treiber. Die App installiert keine Treiber selbst.</summary>
public static class DriverAdvisor
{
    /// <summary>Gerätegruppen, bei denen ein aktueller Treiber spürbar zählt.</summary>
    public static readonly IReadOnlyDictionary<string, string> ImportantClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["DISPLAY"] = "Grafik",
        ["NET"] = "Netzwerk",
        ["MEDIA"] = "Audio",
        ["BLUETOOTH"] = "Bluetooth",
        ["HDC"] = "Speicher",
        ["SCSIADAPTER"] = "Speicher",
        ["SYSTEM"] = "Chipsatz",
    };

    public const string WindowsUpdateOptional = "ms-settings:windowsupdate-optionalupdates";

    /// <summary>Älter als zwei Jahre: ein Blick auf die Herstellerseite lohnt sich.</summary>
    public static bool IsOld(DriverInfo driver, DateTime today) =>
        driver.Date is { } date && date < today.AddYears(-2);

    /// <summary>Offizielle Download-Seite des Herstellers, sonst optionale Updates in Windows Update.</summary>
    public static string LinkFor(string manufacturer)
    {
        var m = manufacturer.ToLowerInvariant();
        if (m.Contains("nvidia")) return "https://www.nvidia.com/de-de/drivers/";
        if (m.Contains("advanced micro devices") || m.Contains("amd") || m.Contains("ati ")) return "https://www.amd.com/de/support/download/drivers.html";
        if (m.Contains("intel")) return "https://www.intel.de/content/www/de/de/support/detect.html";
        if (m.Contains("realtek")) return "https://www.realtek.com/Download/Index";
        return WindowsUpdateOptional;
    }

    /// <summary>Nur wichtige Gruppen, ohne Microsoft-Standardtreiber und ohne doppelte Geräte.</summary>
    public static IReadOnlyList<DriverInfo> Relevant(IEnumerable<DriverInfo> drivers) =>
        drivers.Where(d => ImportantClasses.ContainsKey(d.DeviceClass))
            .Where(d => !string.IsNullOrWhiteSpace(d.Device))
            .Where(d => d.DeviceClass.Equals("DISPLAY", StringComparison.OrdinalIgnoreCase)
                        || !d.Manufacturer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
                        && !d.Manufacturer.StartsWith("(Standard", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => (d.Device, d.Version))
            .Select(g => g.First())
            .OrderBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
