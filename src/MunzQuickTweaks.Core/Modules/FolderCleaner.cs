namespace MunzQuickTweaks.Core.Modules;

public sealed record CleanupResult(int Files, long Bytes, int Skipped)
{
    public static CleanupResult Empty { get; } = new(0, 0, 0);

    public CleanupResult Add(CleanupResult other) => new(Files + other.Files, Bytes + other.Bytes, Skipped + other.Skipped);
}

/// <summary>
/// Löscht Dateien in einem Ordner. Gesperrte Dateien (gerade in Benutzung) werden übersprungen,
/// nie erzwungen. Mit Mindestalter bleiben frische Dateien laufender Installationen unberührt.
/// </summary>
public static class FolderCleaner
{
    public static CleanupResult Measure(string folder, TimeSpan minAge, string pattern = "*") => Run(folder, minAge, pattern, delete: false);

    public static CleanupResult Clean(string folder, TimeSpan minAge, string pattern = "*") => Run(folder, minAge, pattern, delete: true);

    private static CleanupResult Run(string folder, TimeSpan minAge, string pattern, bool delete)
    {
        if (!Directory.Exists(folder))
        {
            return CleanupResult.Empty;
        }

        var cutoff = DateTime.UtcNow - minAge;
        int files = 0, skipped = 0;
        long bytes = 0;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = pattern == "*",
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var path in Directory.EnumerateFiles(folder, pattern, options))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc > cutoff)
                {
                    continue;
                }

                var size = info.Length;
                if (delete)
                {
                    if (info.IsReadOnly)
                    {
                        info.IsReadOnly = false;
                    }

                    info.Delete();
                }

                files++;
                bytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        if (delete && pattern == "*")
        {
            RemoveEmptyFolders(folder);
        }

        return new CleanupResult(files, bytes, skipped);
    }

    private static void RemoveEmptyFolders(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var dir in Directory.EnumerateDirectories(root, "*", options).OrderByDescending(d => d.Length).ToList())
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leerer Ordner in Benutzung: bleibt stehen
            }
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}
