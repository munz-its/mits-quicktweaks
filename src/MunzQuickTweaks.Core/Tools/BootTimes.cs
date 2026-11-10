namespace MunzQuickTweaks.Core.Tools;

/// <summary>Ein Windows-Start. MainPath = bis zum Desktop, PostBoot = bis der Desktop wirklich bedienbar ist.</summary>
public sealed record BootRecord(DateTime Time, long TotalMs, long MainPathMs, long PostBootMs);

public static class BootTimes
{
    /// <summary>Mittelwert der Starts vor und nach einem Zeitpunkt, z. B. dem ersten angewendeten Tweak.</summary>
    public static (double? Before, double? After) Compare(IEnumerable<BootRecord> boots, DateTime since)
    {
        var list = boots.ToList();
        var before = list.Where(b => b.Time < since).Select(b => (double)b.MainPathMs).DefaultIfEmpty().ToList();
        var after = list.Where(b => b.Time >= since).Select(b => (double)b.MainPathMs).DefaultIfEmpty().ToList();
        return (
            list.Any(b => b.Time < since) ? Math.Round(before.Average() / 1000, 1) : null,
            list.Any(b => b.Time >= since) ? Math.Round(after.Average() / 1000, 1) : null);
    }
}
