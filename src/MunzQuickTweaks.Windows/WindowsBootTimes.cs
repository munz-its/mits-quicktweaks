using System.Diagnostics.Eventing.Reader;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

/// <summary>
/// Startzeiten aus dem Windows-Ereignisprotokoll (Diagnostics-Performance, Ereignis 100).
/// Windows misst das selbst bei jedem vollständigen Start, die App liest nur aus.
/// </summary>
public static class WindowsBootTimes
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public static IReadOnlyList<BootRecord> Read(int max = 15)
    {
        var list = new List<BootRecord>();
        var query = new EventLogQuery(LogName, PathType.LogName, "*[System[(EventID=100)]]") { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        for (var record = reader.ReadEvent(); record is not null && list.Count < max; record = reader.ReadEvent())
        {
            using (record)
            {
                if (record.TimeCreated is not { } time || record is not EventLogRecord logRecord)
                {
                    continue;
                }

                var values = logRecord.GetPropertyValues(new EventLogPropertySelector(new[]
                {
                    "Event/EventData/Data[@Name='BootTime']",
                    "Event/EventData/Data[@Name='MainPathBootTime']",
                    "Event/EventData/Data[@Name='BootPostBootTime']",
                }));
                var total = ToMs(values[0]);
                if (total is null or <= 0)
                {
                    continue;
                }

                list.Add(new BootRecord(time, total.Value, ToMs(values[1]) ?? 0, ToMs(values[2]) ?? 0));
            }
        }

        return list;
    }

    private static long? ToMs(object? value) => value switch
    {
        uint u => u,
        ulong ul => (long)ul,
        int i => i,
        long l => l,
        _ => null,
    };
}
