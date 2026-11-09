namespace MunzQuickTweaks.Core.Modules;

/// <summary>Ein Programm auf der Seite Apps.</summary>
/// <param name="Id">Kennung wie im Katalog (Feld "app").</param>
/// <param name="Paths">Programmdateien mit Umgebungsvariablen. Eine davon reicht als "installiert".</param>
/// <param name="StartupMarkers">Teile der Befehlszeile, an denen der Autostart-Eintrag erkannt wird.</param>
public sealed record AppProfile(string Id, string Name, IReadOnlyList<string> Paths, IReadOnlyList<string> StartupMarkers);

public static class AppCatalog
{
    public static readonly IReadOnlyList<AppProfile> All = new[]
    {
        new AppProfile("edge", "Microsoft Edge",
            new[] { @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" },
            new[] { "msedge.exe" }),
        new AppProfile("chrome", "Google Chrome",
            new[] { @"%ProgramFiles%\Google\Chrome\Application\chrome.exe", @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe", @"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe" },
            new[] { "chrome.exe" }),
        new AppProfile("discord", "Discord",
            new[] { @"%LOCALAPPDATA%\Discord\Update.exe" },
            new[] { @"\Discord\Update.exe", "Discord.exe" }),
        new AppProfile("onedrive", "Microsoft OneDrive",
            new[] { @"%LOCALAPPDATA%\Microsoft\OneDrive\OneDrive.exe", @"%ProgramFiles%\Microsoft OneDrive\OneDrive.exe", @"%ProgramFiles(x86)%\Microsoft OneDrive\OneDrive.exe" },
            new[] { "OneDrive.exe" }),
        new AppProfile("steam", "Steam",
            new[] { @"%ProgramFiles(x86)%\Steam\steam.exe", @"%ProgramFiles%\Steam\steam.exe" },
            new[] { "steam.exe" }),
        new AppProfile("epic", "Epic Games Launcher",
            new[] { @"%ProgramFiles(x86)%\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", @"%ProgramFiles(x86)%\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe" },
            new[] { "EpicGamesLauncher.exe" }),
    };

    /// <summary>Autostart-Einträge, die zu diesem Programm gehören. Geplante Aufgaben (z. B. Updater) zählen nicht.</summary>
    public static IReadOnlyList<StartupEntry> MatchStartup(AppProfile app, IEnumerable<StartupEntry> entries) =>
        entries.Where(e => e.Source != StartupSource.ScheduledTask
                           && app.StartupMarkers.Any(m => e.Command.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .ToList();
}
