namespace MunzQuickTweaks.Core.Tools;

public enum HealthLevel
{
    Good,
    Warning,
    Bad,
    Unknown,
}

/// <summary>Datenträger mit SMART-Werten. Fehlende Werte liefert nicht jeder Datenträger.</summary>
public sealed record DiskHealth(string Name, string Media, long SizeBytes, int HealthStatus, int? WearPercent, int? TemperatureC, long? PowerOnHours, long? ReadErrors);

public sealed record BatteryHealth(long DesignCapacity, long FullCapacity, int? Cycles);

public static class HealthAssessment
{
    /// <summary>Windows-Status zuerst, dann Abnutzung (Prozent der Lebensdauer verbraucht) und Lesefehler.</summary>
    public static HealthLevel Disk(DiskHealth disk)
    {
        if (disk.HealthStatus == 2 || disk.WearPercent >= 95 || disk.ReadErrors > 0)
        {
            return HealthLevel.Bad;
        }

        if (disk.HealthStatus == 1 || disk.WearPercent >= 80 || disk.TemperatureC >= 70)
        {
            return HealthLevel.Warning;
        }

        return disk.HealthStatus == 0 ? HealthLevel.Good : HealthLevel.Unknown;
    }

    /// <summary>Restkapazität in Prozent der Werkskapazität.</summary>
    public static int? BatteryPercent(BatteryHealth battery) =>
        battery.DesignCapacity > 0 && battery.FullCapacity > 0
            ? (int)Math.Round(Math.Min(100.0, battery.FullCapacity * 100.0 / battery.DesignCapacity))
            : null;

    public static HealthLevel Battery(BatteryHealth battery) => BatteryPercent(battery) switch
    {
        null => HealthLevel.Unknown,
        >= 80 => HealthLevel.Good,
        >= 60 => HealthLevel.Warning,
        _ => HealthLevel.Bad,
    };
}
