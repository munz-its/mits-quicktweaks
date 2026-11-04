using MunzQuickTweaks.Core;
using Serilog;

namespace MunzQuickTweaks.App.Services;

/// <summary>
/// Zentrales Logging mit Serilog: eine Datei pro Tag unter %ProgramData%\MunzQuickTweaks\logs,
/// 14 Tage aufbewahrt. Wird als Erstes beim Start eingerichtet, damit auch Startfehler im Protokoll landen.
/// </summary>
public static class CrashLog
{
    public static string Directory => AppPaths.LogDirectory;

    /// <summary>Pfad für Fehlermeldungen an den Nutzer.</summary>
    public static string FilePath => Path.Combine(AppPaths.LogDirectory, $"mqt-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialize(string version)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.LogDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(AppPaths.LogDirectory, "mqt-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            Log.Information("Munz Quick Tweaks {Version} gestartet, Windows {OsVersion}", version, Environment.OSVersion.Version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne Schreibrechte läuft die App ohne Protokoll weiter
        }
    }

    public static void Write(string context, Exception exception) => Log.Error(exception, "{Context}", context);

    public static void Close() => Log.CloseAndFlush();
}
