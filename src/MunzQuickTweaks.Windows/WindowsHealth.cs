using System.Diagnostics;
using System.Management;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

/// <summary>SMART-Werte der Datenträger und Akkuzustand aus WMI. Nur lesen.</summary>
public static class WindowsHealth
{
    public static IReadOnlyList<DiskHealth> Disks()
    {
        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        var counters = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reliability = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_StorageReliabilityCounter"));
            foreach (var counter in reliability.Get())
            {
                if (counter["DeviceId"] is string id)
                {
                    counters[id] = counter;
                }
            }
        }
        catch (ManagementException)
        {
            // Ohne Zähler: nur Windows-Status
        }

        var list = new List<DiskHealth>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk"));
        foreach (var disk in searcher.Get())
        {
            using (disk)
            {
                var id = disk["DeviceId"]?.ToString() ?? string.Empty;
                counters.TryGetValue(id, out var counter);
                var media = Convert.ToInt32(disk["MediaType"] ?? 0) switch
                {
                    3 => "HDD",
                    4 => Convert.ToInt32(disk["BusType"] ?? 0) == 17 ? "NVMe SSD" : "SSD",
                    5 => "SCM",
                    _ => Convert.ToInt32(disk["BusType"] ?? 0) == 17 ? "NVMe" : "?",
                };
                list.Add(new DiskHealth(
                    (disk["FriendlyName"] as string ?? "?").Trim(),
                    media,
                    Convert.ToInt64(disk["Size"] ?? 0L),
                    Convert.ToInt32(disk["HealthStatus"] ?? 5),
                    ToInt(counter?["Wear"]),
                    ToInt(counter?["Temperature"]) is > 0 and var t ? t : null,
                    ToLong(counter?["PowerOnHours"]),
                    ToLong(counter?["ReadErrorsUncorrected"])));
            }
        }

        return list;
    }

    /// <summary>Null, wenn kein Akku verbaut ist.</summary>
    public static BatteryHealth? Battery()
    {
        var scope = new ManagementScope(@"\\.\root\wmi");
        var design = First(scope, "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
        var full = First(scope, "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
        if (design is null || full is null)
        {
            return null;
        }

        var cycles = First(scope, "SELECT CycleCount FROM BatteryCycleCount", "CycleCount");
        return new BatteryHealth(design.Value, full.Value, cycles is > 0 ? (int)cycles.Value : null);
    }

    /// <summary>Ausführlicher Akku-Bericht von Windows als HTML auf dem Desktop.</summary>
    public static string? CreateBatteryReport()
    {
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Akku-Bericht.html");
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), $"/batteryreport /output \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        process?.WaitForExit(30_000);
        return File.Exists(target) ? target : null;
    }

    private static long? First(ManagementScope scope, string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    return ToLong(item[property]);
                }
            }
        }
        catch (ManagementException)
        {
            // Klasse fehlt (kein Akku)
        }

        return null;
    }

    private static int? ToInt(object? value) => value is null ? null : Convert.ToInt32(value);

    private static long? ToLong(object? value) => value is null ? null : Convert.ToInt64(value);
}
