using System.Management;
using System.Security.Principal;
using Microsoft.Win32;
using MunzQuickTweaks.Core.Machine;

namespace MunzQuickTweaks.Windows;

/// <summary>Liest Systemdaten aus Registry und WMI. Fehler einzelner Werte führen zu "Unbekannt".</summary>
public sealed class SystemInfoProvider
{
    private const string Unknown = "?";
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    public SystemInfo Read()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var versionKey = hklm.OpenSubKey(CurrentVersionKey);

        var build = ParseInt(versionKey?.GetValue("CurrentBuildNumber") as string);
        var revision = versionKey?.GetValue("UBR") is int ubr ? ubr : 0;
        var edition = versionKey?.GetValue("EditionID") as string;
        var displayVersion = versionKey?.GetValue("DisplayVersion") as string;

        using var cpuKey = hklm.OpenSubKey(CpuKey);
        var cpu = (cpuKey?.GetValue("ProcessorNameString") as string)?.Trim();

        return new SystemInfo(
            OperatingSystem: OsNaming.GetDisplayName(build, revision, edition, displayVersion),
            Build: build,
            IsSupported: OsNaming.IsSupported(build),
            Processor: string.IsNullOrEmpty(cpu) ? Unknown : cpu,
            GraphicsCards: ReadGraphicsCards(),
            MemoryGb: ReadMemoryGb(),
            IsAdministrator: IsAdministrator());
    }

    private static IReadOnlyList<string> ReadGraphicsCards()
    {
        var cards = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var name = (item["Name"] as string)?.Trim();
                    // Virtuelle Adapter (Basisgrafik, Remote-Desktop) sind für Nutzer nicht relevant
                    if (!string.IsNullOrEmpty(name) && !name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                    {
                        cards.Add(name);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // WMI nicht verfügbar: Liste bleibt leer
        }

        return cards;
    }

    private static double ReadMemoryGb()
    {
        try
        {
            ulong total = 0;
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    total += Convert.ToUInt64(item["Capacity"] ?? 0UL);
                }
            }

            return Math.Round(total / 1024d / 1024d / 1024d, 1);
        }
        catch (ManagementException)
        {
            return 0;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int ParseInt(string? value) => int.TryParse(value, out var result) ? result : 0;
}
