using System.Diagnostics;

namespace MunzQuickTweaks.Core.Tweaks;

/// <summary>
/// Dateien und Prozesse für App-Tweaks. Pfade dürfen Umgebungsvariablen wie %APPDATA% enthalten,
/// die Umsetzung löst sie auf. Als Schnittstelle, damit die Engine ohne echtes Dateisystem testbar bleibt.
/// </summary>
public interface IAppEnvironment
{
    bool FileExists(string path);

    /// <summary>Null, wenn die Datei fehlt.</summary>
    string? ReadText(string path);

    void WriteText(string path, string text);

    bool IsProcessRunning(string processName);
}

/// <summary>Echtes Dateisystem und echte Prozessliste.</summary>
public sealed class LocalAppEnvironment : IAppEnvironment
{
    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);

    public bool FileExists(string path) => File.Exists(Expand(path));

    public string? ReadText(string path)
    {
        var full = Expand(path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    public void WriteText(string path, string text)
    {
        // Erst in eine Nachbardatei, dann ersetzen: eine halb geschriebene Einstellungsdatei würde das Programm zurücksetzen
        var full = Expand(path);
        var temp = full + ".munz.tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, full, overwrite: true);
    }

    public bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
