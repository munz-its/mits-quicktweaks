using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace MunzQuickTweaks.Windows;

/// <summary>
/// Geplante Aufgabe: kurz nach dem Anmelden startet die App unsichtbar mit --check-reset.
/// Sie meldet sich nur, wenn ein Windows-Update Tweaks zurückgesetzt hat. Kein Dauerprozess.
/// </summary>
public static class ResetCheckTask
{
    public const string Folder = "Munz IT Service";
    public const string Name = "Quick Tweaks Update-Pruefung";
    public const string Argument = "--check-reset";

    private static string FullPath => $@"\{Folder}\{Name}";

    /// <summary>Legt die Aufgabe an oder aktualisiert den Pfad zur exe (die App ist portabel und kann verschoben werden).</summary>
    public static void Ensure(bool enabled, string exePath)
    {
        using var service = new TaskService();
        if (!enabled)
        {
            if (service.RootFolder.SubFolders.Exists(Folder))
            {
                service.GetFolder($@"\{Folder}")?.DeleteTask(Name, exceptionOnNotExists: false);
            }

            return;
        }

        using (var current = service.GetTask(FullPath))
        {
            if (current?.Definition.Actions.OfType<ExecAction>().FirstOrDefault() is { } exec
                && string.Equals(exec.Path, exePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var user = WindowsIdentity.GetCurrent().Name;
        var definition = service.NewTask();
        definition.RegistrationInfo.Description = "Prüft nach dem Anmelden, ob ein Windows-Update Einstellungen von Munz Quick Tweaks zurückgesetzt hat.";
        definition.RegistrationInfo.Author = "Munz IT Service";
        definition.Principal.UserId = user;
        definition.Principal.LogonType = TaskLogonType.InteractiveToken;
        definition.Principal.RunLevel = TaskRunLevel.Highest;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = TimeSpan.FromHours(2);
        definition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        definition.Triggers.Add(new LogonTrigger { UserId = user, Delay = TimeSpan.FromMinutes(2) });
        definition.Actions.Add(new ExecAction(exePath, Argument, Path.GetDirectoryName(exePath)));

        var folder = service.RootFolder.SubFolders.Exists(Folder)
            ? service.GetFolder($@"\{Folder}")
            : service.RootFolder.CreateFolder(Folder);
        folder.RegisterTaskDefinition(Name, definition, TaskCreation.CreateOrUpdate, user, null, TaskLogonType.InteractiveToken);
    }
}
