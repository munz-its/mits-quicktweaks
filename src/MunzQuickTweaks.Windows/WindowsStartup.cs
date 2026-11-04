using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.Windows;

/// <summary>Autostart-Einträge aus Registry, Autostart-Ordnern und geplanten Aufgaben.</summary>
public sealed class WindowsStartup : IStartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private readonly bool _includeUserLevel;

    /// <param name="includeUserLevel">False bei fremdem Admin-Konto: Benutzer-Einträge gehören dann zum falschen Konto.</param>
    public WindowsStartup(bool includeUserLevel)
    {
        _includeUserLevel = includeUserLevel;
    }

    public IReadOnlyList<StartupEntry> List()
    {
        var entries = new List<StartupEntry>();
        if (_includeUserLevel)
        {
            ReadRegistry(entries, RegistryHive.CurrentUser, RunKey, "Run", StartupSource.UserRegistry);
            ReadFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, StartupSource.UserFolder);
        }

        ReadRegistry(entries, RegistryHive.LocalMachine, RunKey, "Run", StartupSource.MachineRegistry);
        ReadRegistry(entries, RegistryHive.LocalMachine, Run32Key, "Run32", StartupSource.MachineRegistry32);
        ReadFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, StartupSource.CommonFolder);
        ReadTasks(entries);

        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public OperationResult SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            if (entry.Source == StartupSource.ScheduledTask)
            {
                using var service = new TaskService();
                var task = service.GetTask(entry.Id) ?? throw new InvalidOperationException("Aufgabe nicht gefunden");
                task.Enabled = enabled;
                return OperationResult.Ok;
            }

            var (hive, approvedKey, valueName) = Approved(entry);
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.CreateSubKey(approvedKey, writable: true);
            key.SetValue(valueName, StartupApproved.Create(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
            return OperationResult.Ok;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    // Id-Aufbau: "<Quelle>|<Wertname oder Dateiname>"
    private static (RegistryHive Hive, string Key, string Value) Approved(StartupEntry entry)
    {
        var name = entry.Id[(entry.Id.IndexOf('|') + 1)..];
        return entry.Source switch
        {
            StartupSource.UserRegistry => (RegistryHive.CurrentUser, ApprovedBase + "Run", name),
            StartupSource.MachineRegistry => (RegistryHive.LocalMachine, ApprovedBase + "Run", name),
            StartupSource.MachineRegistry32 => (RegistryHive.LocalMachine, ApprovedBase + "Run32", name),
            StartupSource.UserFolder => (RegistryHive.CurrentUser, ApprovedBase + "StartupFolder", name),
            StartupSource.CommonFolder => (RegistryHive.LocalMachine, ApprovedBase + "StartupFolder", name),
            _ => throw new InvalidOperationException("Unbekannte Quelle"),
        };
    }

    private static void ReadRegistry(List<StartupEntry> entries, RegistryHive hive, string runKey, string approvedName, StartupSource source)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var run = root.OpenSubKey(runKey);
            if (run is null)
            {
                return;
            }

            using var approved = root.OpenSubKey(ApprovedBase + approvedName);
            foreach (var name in run.GetValueNames().Where(n => n.Length > 0))
            {
                var command = run.GetValue(name)?.ToString() ?? string.Empty;
                var enabled = StartupApproved.IsEnabled(approved?.GetValue(name) as byte[]);
                entries.Add(new StartupEntry($"{source}|{name}", name, command, source, enabled));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Nicht lesbar: Quelle überspringen
        }
    }

    private static void ReadFolder(List<StartupEntry> entries, string folder, RegistryHive hive, StartupSource source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var approved = root.OpenSubKey(ApprovedBase + "StartupFolder");
        foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
        {
            var fileName = Path.GetFileName(file);
            var enabled = StartupApproved.IsEnabled(approved?.GetValue(fileName) as byte[]);
            entries.Add(new StartupEntry($"{source}|{fileName}", Path.GetFileNameWithoutExtension(file), file, source, enabled));
        }
    }

    /// <summary>Nur Aufgaben außerhalb von \Microsoft mit Anmelde- oder Start-Auslöser.</summary>
    private static void ReadTasks(List<StartupEntry> entries)
    {
        try
        {
            using var service = new TaskService();
            foreach (var task in service.AllTasks)
            {
                using (task)
                {
                    if (task.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var atStart = task.Definition.Triggers.Any(t => t.TriggerType is TaskTriggerType.Logon or TaskTriggerType.Boot);
                    if (!atStart)
                    {
                        continue;
                    }

                    var command = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault() is { } exec
                        ? $"{exec.Path} {exec.Arguments}".Trim()
                        : task.Definition.Actions.FirstOrDefault()?.ToString() ?? string.Empty;
                    entries.Add(new StartupEntry(task.Path, task.Name, command, StartupSource.ScheduledTask, task.Enabled));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or FileNotFoundException)
        {
            // Aufgabenplanung nicht erreichbar: ohne Aufgaben weiter
        }
    }
}
