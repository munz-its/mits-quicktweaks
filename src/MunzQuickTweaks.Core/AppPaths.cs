namespace MunzQuickTweaks.Core;

/// <summary>Zentrale Ablageorte der App.</summary>
public static class AppPaths
{
    private const string FolderName = "MunzQuickTweaks";

    /// <summary>Einstellungen pro Benutzer: %LocalAppData%\MunzQuickTweaks</summary>
    public static string UserData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary>Journal, Backups und Logs für alle Benutzer: %ProgramData%\MunzQuickTweaks</summary>
    public static string MachineData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), FolderName);

    public static string SettingsFile => Path.Combine(UserData, "settings.json");

    public static string LogDirectory => Path.Combine(MachineData, "logs");
}
