using System.Text;
using MunzQuickTweaks.Core;

namespace MunzQuickTweaks.App.Services;

/// <summary>
/// Einfaches Fehlerprotokoll für Phase 1. Wird in Phase 3 durch das vollständige Logging ersetzt.
/// </summary>
public static class CrashLog
{
    public static string FilePath => Path.Combine(AppPaths.LogDirectory, "error.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var entry = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .AppendLine(context)
                .AppendLine(exception.ToString())
                .AppendLine();
            File.AppendAllText(FilePath, entry.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Protokollieren darf nie selbst einen Absturz auslösen
        }
    }
}
