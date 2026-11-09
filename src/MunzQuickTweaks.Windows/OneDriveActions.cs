using System.Diagnostics;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.Windows;

/// <summary>OneDrive entfernen und wieder installieren, über das offizielle Setup von Microsoft.</summary>
public static class OneDriveActions
{
    public const string DownloadPage = "https://www.microsoft.com/microsoft-365/onedrive/download";

    /// <summary>OneDriveSetup.exe aus Windows oder aus der installierten OneDrive-Version.</summary>
    public static string? FindSetup()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var fixedPaths = new[]
        {
            Path.Combine(windows, "System32", "OneDriveSetup.exe"),
            Path.Combine(windows, "SysWOW64", "OneDriveSetup.exe"),
        };

        var found = fixedPaths.FirstOrDefault(File.Exists);
        if (found is not null)
        {
            return found;
        }

        // Neuere Windows-Versionen bringen das Setup nur im Versionsordner von OneDrive mit
        foreach (var root in new[]
                 {
                     Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\OneDrive"),
                     Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft OneDrive"),
                     Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft OneDrive"),
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var setup = Directory.EnumerateDirectories(root)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, "OneDriveSetup.exe"))
                .FirstOrDefault(File.Exists);
            if (setup is not null)
            {
                return setup;
            }
        }

        return null;
    }

    public static async Task<OperationResult> UninstallAsync()
    {
        var setup = FindSetup();
        if (setup is null)
        {
            return OperationResult.Fail("Das OneDrive-Setup wurde nicht gefunden.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(setup, "/uninstall") { UseShellExecute = false, CreateNoWindow = true });
            if (process is null)
            {
                return OperationResult.Fail("OneDrive-Setup ließ sich nicht starten.");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? OperationResult.Ok : OperationResult.Fail($"OneDrive-Setup meldet Code {process.ExitCode}.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Startet das Setup über den Explorer, damit es ohne Administratorrechte läuft:
    /// OneDrive installiert sich pro Benutzer. Ohne Setup öffnet sich die Download-Seite.
    /// </summary>
    public static bool StartInstall()
    {
        var setup = FindSetup();
        try
        {
            if (setup is not null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{setup}\"") { UseShellExecute = false })?.Dispose();
                return true;
            }

            Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true })?.Dispose();
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
