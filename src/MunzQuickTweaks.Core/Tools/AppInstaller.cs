using System.Text.Json;

namespace MunzQuickTweaks.Core.Tools;

public enum InstallGroup
{
    Browser,
    Communication,
    Gaming,
    Media,
    Tools,
    Office,
    Development,
}

/// <summary>Programm aus dem offiziellen winget-Katalog von Microsoft. Installiert wird immer von der Quelle des Herstellers.</summary>
public sealed record InstallableApp(string WingetId, string Name, InstallGroup Group);

public sealed record InstallResult(bool Success, bool AlreadyInstalled, string? Error);

public static class InstallCatalog
{
    public static readonly IReadOnlyList<InstallableApp> All = new[]
    {
        new InstallableApp("Google.Chrome", "Google Chrome", InstallGroup.Browser),
        new InstallableApp("Mozilla.Firefox", "Mozilla Firefox", InstallGroup.Browser),
        new InstallableApp("Brave.Brave", "Brave", InstallGroup.Browser),
        new InstallableApp("Opera.OperaGX", "Opera GX", InstallGroup.Browser),
        new InstallableApp("Vivaldi.Vivaldi", "Vivaldi", InstallGroup.Browser),

        new InstallableApp("Discord.Discord", "Discord", InstallGroup.Communication),
        new InstallableApp("TeamSpeakSystems.TeamSpeakClient", "TeamSpeak", InstallGroup.Communication),
        new InstallableApp("Zoom.Zoom", "Zoom", InstallGroup.Communication),
        new InstallableApp("Microsoft.Teams", "Microsoft Teams", InstallGroup.Communication),
        new InstallableApp("Telegram.TelegramDesktop", "Telegram", InstallGroup.Communication),
        new InstallableApp("OpenWhisperSystems.Signal", "Signal", InstallGroup.Communication),

        new InstallableApp("Valve.Steam", "Steam", InstallGroup.Gaming),
        new InstallableApp("EpicGames.EpicGamesLauncher", "Epic Games Launcher", InstallGroup.Gaming),
        new InstallableApp("ElectronicArts.EADesktop", "EA app", InstallGroup.Gaming),
        new InstallableApp("Ubisoft.Connect", "Ubisoft Connect", InstallGroup.Gaming),
        new InstallableApp("GOG.Galaxy", "GOG Galaxy", InstallGroup.Gaming),
        new InstallableApp("Blizzard.BattleNet", "Battle.net", InstallGroup.Gaming),
        new InstallableApp("Playnite.Playnite", "Playnite", InstallGroup.Gaming),

        new InstallableApp("VideoLAN.VLC", "VLC media player", InstallGroup.Media),
        new InstallableApp("OBSProject.OBSStudio", "OBS Studio", InstallGroup.Media),
        new InstallableApp("Audacity.Audacity", "Audacity", InstallGroup.Media),
        new InstallableApp("dotPDN.PaintDotNet", "Paint.NET", InstallGroup.Media),
        new InstallableApp("HandBrake.HandBrake", "HandBrake", InstallGroup.Media),

        new InstallableApp("7zip.7zip", "7-Zip", InstallGroup.Tools),
        new InstallableApp("voidtools.Everything", "Everything", InstallGroup.Tools),
        new InstallableApp("Microsoft.PowerToys", "PowerToys", InstallGroup.Tools),
        new InstallableApp("Notepad++.Notepad++", "Notepad++", InstallGroup.Tools),
        new InstallableApp("ShareX.ShareX", "ShareX", InstallGroup.Tools),
        new InstallableApp("WinDirStat.WinDirStat", "WinDirStat", InstallGroup.Tools),
        new InstallableApp("CPUID.CPU-Z", "CPU-Z", InstallGroup.Tools),
        new InstallableApp("TechPowerUp.GPU-Z", "GPU-Z", InstallGroup.Tools),
        new InstallableApp("REALiX.HWiNFO", "HWiNFO", InstallGroup.Tools),
        new InstallableApp("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", InstallGroup.Tools),
        new InstallableApp("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", InstallGroup.Tools),
        new InstallableApp("Bitwarden.Bitwarden", "Bitwarden", InstallGroup.Tools),
        new InstallableApp("KeePassXCTeam.KeePassXC", "KeePassXC", InstallGroup.Tools),

        new InstallableApp("TheDocumentFoundation.LibreOffice", "LibreOffice", InstallGroup.Office),
        new InstallableApp("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", InstallGroup.Office),
        new InstallableApp("SumatraPDF.SumatraPDF", "SumatraPDF", InstallGroup.Office),
        new InstallableApp("Notion.Notion", "Notion", InstallGroup.Office),
        new InstallableApp("Obsidian.Obsidian", "Obsidian", InstallGroup.Office),

        new InstallableApp("Microsoft.VisualStudioCode", "Visual Studio Code", InstallGroup.Development),
        new InstallableApp("Git.Git", "Git", InstallGroup.Development),
        new InstallableApp("OpenJS.NodeJS.LTS", "Node.js LTS", InstallGroup.Development),
        new InstallableApp("Python.Python.3.13", "Python 3.13", InstallGroup.Development),
        new InstallableApp("Microsoft.WindowsTerminal", "Windows Terminal", InstallGroup.Development),
    };

    /// <summary>Paketkennungen aus "winget export" (JSON). Kaputte Dateien ergeben eine leere Liste.</summary>
    public static HashSet<string> ParseExport(string json)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Sources", out var sources))
            {
                return ids;
            }

            foreach (var source in sources.EnumerateArray())
            {
                if (!source.TryGetProperty("Packages", out var packages))
                {
                    continue;
                }

                foreach (var package in packages.EnumerateArray())
                {
                    if (package.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { } text)
                    {
                        ids.Add(text);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Unbekanntes Format: alles als nicht installiert anzeigen
        }

        return ids;
    }

    /// <summary>winget-Rückgabecodes: 0 = installiert, "schon installiert" zählt ebenfalls als Erfolg.</summary>
    public static InstallResult Interpret(int exitCode, string output)
    {
        const int AlreadyInstalled = unchecked((int)0x8A15002B);
        const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
        if (exitCode == 0)
        {
            return new InstallResult(true, false, null);
        }

        if (exitCode is AlreadyInstalled or PackageAlreadyInstalled)
        {
            return new InstallResult(true, true, null);
        }

        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.All(c => c is '-' or '\\' or '|' or '/' or ' '));
        return new InstallResult(false, false, $"Code 0x{exitCode:X8}" + (line is null ? string.Empty : $": {line}"));
    }
}

/// <summary>Installiertes Programm, für das winget eine neuere Version kennt.</summary>
public sealed record UpgradablePackage(string Name, string Id, string Version, string Available);

/// <summary>
/// Liest die Tabelle von "winget upgrade". Die Spaltenköpfe sind übersetzt, darum zählt nur ihre Position:
/// Name, Kennung, Version, Verfügbar, Quelle. Gekürzte Kennungen ("…") werden über die vollständige Liste aus "winget export" aufgelöst.
/// </summary>
public static class UpgradeParser
{
    /// <summary>winget aktualisiert sich nicht selbst aus der App heraus, sonst bricht der laufende Vorgang ab.</summary>
    public static readonly IReadOnlySet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.AppInstaller",
        "Microsoft.DesktopAppInstaller",
    };

    public static IReadOnlyList<UpgradablePackage> Parse(string output, IReadOnlyCollection<string>? knownIds = null)
    {
        var result = new List<UpgradablePackage>();

        // Fortschrittsanzeige von winget arbeitet mit \r. Pro Zeile zählt nur der letzte Abschnitt
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
            .ToList();

        var separator = lines.FindIndex(l => l.Trim().Length >= 10 && l.Trim().All(c => c == '-'));
        if (separator < 1)
        {
            return result;
        }

        var header = lines[separator - 1];
        var starts = ColumnStarts(header);
        if (starts.Count < 4)
        {
            return result;
        }

        for (var i = separator + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                break; // Ende der ersten Tabelle, danach folgen Hinweise oder eine zweite Liste
            }

            if (line.Length < starts[3])
            {
                continue; // Fußzeile wie "3 Aktualisierungen verfügbar."
            }

            var name = Cell(line, starts, 0);
            var id = Cell(line, starts, 1);
            var version = Cell(line, starts, 2);
            var available = Cell(line, starts, 3);
            if (id.Length == 0 || available.Length == 0 || id.Contains(' ', StringComparison.Ordinal))
            {
                continue;
            }

            if (id.EndsWith('…') && knownIds is not null)
            {
                var prefix = id.TrimEnd('…');
                var matches = knownIds.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (matches.Count != 1)
                {
                    continue; // nicht eindeutig: lieber weglassen als das falsche Programm aktualisieren
                }

                id = matches[0];
            }
            else if (id.EndsWith('…'))
            {
                continue;
            }

            if (Excluded.Contains(id))
            {
                continue;
            }

            result.Add(new UpgradablePackage(name.TrimEnd('…').Trim(), id, version, available));
        }

        return result;
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    private static string Cell(string line, List<int> starts, int column)
    {
        var start = starts[column];
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var end = column + 1 < starts.Count ? Math.Min(starts[column + 1], line.Length) : line.Length;
        return line[start..end].Trim();
    }
}
