using System.Diagnostics;
using System.Text;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

/// <summary>Installation über winget (App-Installer von Microsoft). Ohne winget bietet die App den Store-Link an.</summary>
public static class WindowsWinget
{
    public const string StoreLink = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1";

    public static string? Find()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(local))
        {
            return local;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "winget.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Installierte Programme, die winget kennt.</summary>
    public static HashSet<string> Installed(string winget)
    {
        var file = Path.Combine(Path.GetTempPath(), $"munz-winget-{Guid.NewGuid():N}.json");
        try
        {
            Run(winget, new[] { "export", "-o", file, "--accept-source-agreements", "--disable-interactivity" }, TimeSpan.FromMinutes(3));
            return File.Exists(file) ? InstallCatalog.ParseExport(File.ReadAllText(file)) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Temp-Datei bleibt liegen
            }
        }
    }

    public static InstallResult Install(string winget, InstallableApp app, CancellationToken cancel)
    {
        var (code, output) = Run(winget, new[]
        {
            "install", "--id", app.WingetId, "--exact", "--silent", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        }, TimeSpan.FromMinutes(20), cancel);
        return InstallCatalog.Interpret(code, output);
    }

    private static (int Code, string Output) Run(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken cancel = default)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("winget ließ sich nicht starten.");
        var output = process.StandardOutput.ReadToEndAsync(cancel);
        var error = process.StandardError.ReadToEndAsync(cancel);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeoutSource.CancelAfter(timeout);
        try
        {
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancel.ThrowIfCancellationRequested();
            throw new TimeoutException("winget hat zu lange gebraucht.");
        }

        return (process.ExitCode, output.Result + error.Result);
    }
}
