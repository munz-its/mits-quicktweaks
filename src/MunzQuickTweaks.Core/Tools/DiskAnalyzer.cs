namespace MunzQuickTweaks.Core.Tools;

public sealed record DiskItem(string Path, string Name, long Size, bool IsDirectory, long Files);

/// <summary>
/// Zeigt, welche Ordner und Dateien auf einem Laufwerk am meisten Platz belegen.
/// Nur lesen: der Analyzer löscht nichts. Verknüpfte Ordner (Junctions) werden übersprungen, damit nichts doppelt zählt.
/// </summary>
public static class DiskAnalyzer
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Direkte Einträge eines Ordners mit Gesamtgröße, größte zuerst.</summary>
    /// <param name="progress">Meldet den Ordner, der gerade gezählt wird.</param>
    public static IReadOnlyList<DiskItem> Scan(string folder, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var root = new DirectoryInfo(folder);
        var items = new List<DiskItem>();

        foreach (var dir in SafeDirectories(root))
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(dir.FullName);
            var (size, files) = Measure(dir, cancel);
            items.Add(new DiskItem(dir.FullName, dir.Name, size, true, files));
        }

        foreach (var file in SafeFiles(root))
        {
            items.Add(new DiskItem(file.FullName, file.Name, SafeLength(file), false, 1));
        }

        return items.OrderByDescending(i => i.Size).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Größe eines Ordners samt Unterordnern, ohne Rekursion auf dem Stack.</summary>
    public static (long Size, long Files) Measure(DirectoryInfo folder, CancellationToken cancel = default)
    {
        long size = 0;
        long files = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var current = stack.Pop();
            foreach (var file in SafeFiles(current))
            {
                size += SafeLength(file);
                files++;
            }

            foreach (var sub in SafeDirectories(current))
            {
                stack.Push(sub);
            }
        }

        return (size, files);
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateDirectories("*", Options).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return Array.Empty<DirectoryInfo>();
        }
    }

    private static IEnumerable<FileInfo> SafeFiles(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles("*", Options).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Lesbare Größe, z. B. "1,4 GB".</summary>
    public static string FormatSize(long bytes, IFormatProvider? culture = null)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.0";
        return value.ToString(format, culture ?? System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
