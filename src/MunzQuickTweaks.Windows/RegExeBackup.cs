using System.Diagnostics;
using System.Text;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Windows;

/// <summary>Exportiert Schlüssel mit dem Windows-Bordmittel reg.exe. Die .reg-Datei lässt sich per Doppelklick zurückspielen.</summary>
public sealed class RegExeBackup : IRegistryBackup
{
    private const string Header = "Windows Registry Editor Version 5.00";
    private readonly string _directory;
    private readonly IRegistry _registry;

    public RegExeBackup(string directory, IRegistry registry)
    {
        _directory = directory;
        _registry = registry;
    }

    public string? Export(string tweakId, IReadOnlyCollection<(RegistryHive Hive, string Key)> keys)
    {
        var existing = keys.Where(k => _registry.KeyExists(k.Hive, k.Key)).ToList();
        if (existing.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, $"{DateTime.Now:yyyyMMdd-HHmmss}_{tweakId}.reg");
        var combined = new StringBuilder().AppendLine(Header).AppendLine();

        foreach (var (hive, key) in existing)
        {
            var temp = Path.Combine(Path.GetTempPath(), $"mqt-{Guid.NewGuid():N}.reg");
            try
            {
                var regExe = Path.Combine(Environment.SystemDirectory, "reg.exe");
                using var process = Process.Start(new ProcessStartInfo(regExe)
                {
                    ArgumentList = { "export", $"{hive}\\{key}", temp, "/y", "/reg:64" },
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }) ?? throw new IOException("reg.exe konnte nicht gestartet werden");

                if (!process.WaitForExit(15000) || process.ExitCode != 0)
                {
                    throw new IOException($"reg.exe export {hive}\\{key}: {process.StandardError.ReadToEnd().Trim()}");
                }

                // reg.exe schreibt UTF-16; Kopfzeile nur einmal übernehmen
                foreach (var line in File.ReadAllLines(temp, Encoding.Unicode))
                {
                    if (!line.StartsWith(Header, StringComparison.Ordinal))
                    {
                        combined.AppendLine(line);
                    }
                }
            }
            finally
            {
                File.Delete(temp);
            }
        }

        File.WriteAllText(target, combined.ToString(), Encoding.Unicode);
        return target;
    }
}
