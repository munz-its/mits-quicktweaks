using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MunzQuickTweaks.Windows;

/// <summary>Explorer neu starten, abmelden, neu starten: damit Tweaks sofort wirken.</summary>
public static partial class SessionActions
{
    private const uint EwxLogoff = 0x00000000;
    private const uint ShtdnReasonFlagPlanned = 0x80000000;

    /// <summary>
    /// Beendet den Explorer. Windows startet ihn als normaler Nutzer selbst neu (AutoRestartShell).
    /// Nur falls das ausbleibt, wird er manuell gestartet.
    /// </summary>
    public static async Task RestartExplorerAsync()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Bereits beendet oder geschützt
                }
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(4));
        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            {
                UseShellExecute = true,
            })?.Dispose();
        }
    }

    public static bool SignOut() => ExitWindowsEx(EwxLogoff, ShtdnReasonFlagPlanned);

    public static void Reboot()
    {
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        {
            ArgumentList = { "/r", "/t", "5", "/c", "Munz Quick Tweaks: Neustart, damit die Änderungen wirken." },
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExitWindowsEx(uint flags, uint reason);
}
