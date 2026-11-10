using System.Diagnostics;
using System.Runtime.InteropServices;
using MunzQuickTweaks.Core.Modules;

namespace MunzQuickTweaks.Windows;

public enum CleanupTarget
{
    UserTemp,
    WindowsTemp,
    UpdateCache,
    ErrorReports,
    ThumbnailCache,
    ShaderCache,
    BrowserCache,
    AppCache,
    RecycleBin,
}

public enum RepairTool
{
    Sfc,
    Dism,
    UpdateReset,
}

/// <summary>Aufräumen und Reparatur mit Windows-Bordmitteln.</summary>
public sealed partial class WindowsCleanup
{
    // Frische Temp-Dateien gehören oft zu laufenden Installationen
    private static readonly TimeSpan TempMinAge = TimeSpan.FromHours(24);

    private static string Windir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static (string Folder, TimeSpan MinAge, string Pattern)[] Folders(CleanupTarget target) => target switch
    {
        CleanupTarget.UserTemp => new[] { (Path.GetTempPath(), TempMinAge, "*") },
        CleanupTarget.WindowsTemp => new[] { (Path.Combine(Windir, "Temp"), TempMinAge, "*") },
        CleanupTarget.UpdateCache => new[] { (Path.Combine(Windir, "SoftwareDistribution", "Download"), TimeSpan.Zero, "*") },
        CleanupTarget.ErrorReports => new[]
        {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"), TimeSpan.Zero, "*"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"), TimeSpan.Zero, "*"),
        },
        CleanupTarget.ThumbnailCache => new[]
        {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"), TimeSpan.Zero, "thumbcache_*.db"),
        },
        // Grafiktreiber bauen Shader beim nächsten Spielstart neu, das erste Laden dauert dann etwas länger
        CleanupTarget.ShaderCache => new[]
        {
            Local("D3DSCache"), Local("NVIDIA", "DXCache"), Local("NVIDIA", "GLCache"), Local("NVIDIA Corporation", "NV_Cache"),
            Local("AMD", "DxCache"), Local("AMD", "DxcCache"), Local("AMD", "VkCache"), Local("AMD", "GLCache"),
            Local("Intel", "ShaderCache"),
        },
        CleanupTarget.BrowserCache => BrowserCaches().ToArray(),
        CleanupTarget.AppCache => new[]
        {
            Roaming("discord", "Cache"), Roaming("discord", "Code Cache"), Roaming("discord", "GPUCache"),
            Local("Steam", "htmlcache"),
            Local("Spotify", "Data"),
            Roaming("Microsoft", "Teams", "Cache"),
        },
        _ => Array.Empty<(string, TimeSpan, string)>(),
    };

    private static (string, TimeSpan, string) Local(params string[] parts) =>
        (Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Concat(parts).ToArray()), TimeSpan.Zero, "*");

    private static (string, TimeSpan, string) Roaming(params string[] parts) =>
        (Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) }.Concat(parts).ToArray()), TimeSpan.Zero, "*");

    /// <summary>Cache aller Profile von Chrome, Edge, Brave und Firefox. Lesezeichen, Passwörter und Verlauf bleiben unberührt.</summary>
    private static IEnumerable<(string, TimeSpan, string)> BrowserCaches()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var userData in new[]
                 {
                     Path.Combine(local, "Google", "Chrome", "User Data"),
                     Path.Combine(local, "Microsoft", "Edge", "User Data"),
                     Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
                 })
        {
            foreach (var profile in SafeDirectories(userData).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
            {
                yield return (Path.Combine(profile, "Cache", "Cache_Data"), TimeSpan.Zero, "*");
                yield return (Path.Combine(profile, "Code Cache"), TimeSpan.Zero, "*");
                yield return (Path.Combine(profile, "GPUCache"), TimeSpan.Zero, "*");
            }
        }

        foreach (var profile in SafeDirectories(Path.Combine(local, "Mozilla", "Firefox", "Profiles")))
        {
            yield return (Path.Combine(profile, "cache2"), TimeSpan.Zero, "*");
        }
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public long Measure(CleanupTarget target)
    {
        if (target == CleanupTarget.RecycleBin)
        {
            var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
            return SHQueryRecycleBinW(null, ref info) == 0 ? info.TotalSize : 0;
        }

        return Folders(target).Sum(f => FolderCleaner.Measure(f.Folder, f.MinAge, f.Pattern).Bytes);
    }

    public CleanupResult Clean(CleanupTarget target)
    {
        if (target == CleanupTarget.RecycleBin)
        {
            var size = Measure(target);
            const int NoConfirmation = 0x1, NoProgress = 0x2, NoSound = 0x4;
            var code = SHEmptyRecycleBinW(IntPtr.Zero, null, NoConfirmation | NoProgress | NoSound);
            return code == 0 ? new CleanupResult(0, size, 0) : CleanupResult.Empty;
        }

        return Folders(target).Aggregate(CleanupResult.Empty, (sum, f) => sum.Add(FolderCleaner.Clean(f.Folder, f.MinAge, f.Pattern)));
    }

    public bool FlushDns() => DnsFlushResolverCache();

    /// <summary>Startet ein sichtbares Konsolenfenster, damit der Fortschritt von SFC und DISM erkennbar bleibt.</summary>
    public void StartRepair(RepairTool tool)
    {
        var script = tool switch
        {
            RepairTool.Sfc => "sfc /scannow",
            RepairTool.Dism => "DISM /Online /Cleanup-Image /RestoreHealth",
            RepairTool.UpdateReset =>
                "net stop wuauserv & net stop bits & net stop cryptsvc & " +
                $"ren {Windir}\\SoftwareDistribution SoftwareDistribution.bak-%RANDOM% & " +
                $"ren {Windir}\\System32\\catroot2 catroot2.bak-%RANDOM% & " +
                "net start cryptsvc & net start bits & net start wuauserv",
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };

        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Process.Start(new ProcessStartInfo(cmd, $"/c \"title Munz Quick Tweaks & {script} & echo. & pause\"")
        {
            UseShellExecute = true,
        })?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long TotalSize;
        public long ItemCount;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string? rootPath, ref ShQueryRbInfo info);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, int flags);

    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();
}
