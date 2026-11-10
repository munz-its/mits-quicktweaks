namespace MunzQuickTweaks.Core.Settings;

/// <summary>Sprache der Oberfläche. System folgt der Windows-Anzeigesprache.</summary>
public enum AppLanguage
{
    System,
    German,
    English,
}

/// <summary>Benutzereinstellungen, gespeichert als JSON pro Windows-Benutzer.</summary>
public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Logo-Aufbau beim Start, Pixel-Wisch und weitere Animationen.</summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>Beim Start auf munz-its.de nach einer neuen Version sehen.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Nach dem Anmelden prüfen, ob ein Windows-Update Tweaks zurückgesetzt hat.</summary>
    public bool CheckResetAfterUpdates { get; set; } = true;
}
