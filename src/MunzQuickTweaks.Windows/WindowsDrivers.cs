using System.Management;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

/// <summary>Installierte Treiber aus WMI (Win32_PnPSignedDriver).</summary>
public static class WindowsDrivers
{
    public static IReadOnlyList<DriverInfo> List()
    {
        var list = new List<DriverInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned FROM Win32_PnPSignedDriver");
        foreach (var item in searcher.Get().Cast<ManagementObject>())
        {
            using (item)
            {
                DateTime? date = null;
                if (item["DriverDate"] is string raw && raw.Length >= 8)
                {
                    try
                    {
                        date = ManagementDateTimeConverter.ToDateTime(raw).Date;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Datum unlesbar: ohne Datum anzeigen
                    }
                }

                list.Add(new DriverInfo(
                    (item["DeviceName"] as string ?? string.Empty).Trim(),
                    (item["DeviceClass"] as string ?? string.Empty).Trim(),
                    (item["Manufacturer"] as string ?? string.Empty).Trim(),
                    (item["DriverVersion"] as string ?? string.Empty).Trim(),
                    date,
                    item["IsSigned"] is true));
            }
        }

        return list;
    }
}
