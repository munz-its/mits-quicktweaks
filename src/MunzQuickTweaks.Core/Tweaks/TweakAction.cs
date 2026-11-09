using System.Text.Json;
using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Tweaks;

public enum RegistryHive
{
    HKCU,
    HKLM,
}

public enum RegistryKind
{
    DWord,
    QWord,
    String,
    ExpandString,
}

public enum ServiceStartType
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
}

/// <summary>
/// Basis aller Aktionen. Es gibt bewusst keine freie Kommandozeile:
/// ein Katalogeintrag kann nur genau definierte, umkehrbare Änderungen auslösen.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryAction), "registry")]
[JsonDerivedType(typeof(ServiceAction), "service")]
[JsonDerivedType(typeof(PowerPlanAction), "powerPlan")]
[JsonDerivedType(typeof(JsonSettingAction), "jsonSetting")]
public abstract class TweakAction
{
}

/// <summary>Setzt einen Registry-Wert.</summary>
public sealed class RegistryAction : TweakAction
{
    public RegistryHive Hive { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public RegistryKind Kind { get; set; }

    /// <summary>Zielwert als Text, z. B. "0" für DWord 0.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Windows-Standardwert. Wird für Rückgängig genutzt, wenn kein Journal-Eintrag existiert
    /// (z. B. Tweak wurde außerhalb der App gesetzt). Null = Wert löschen.
    /// </summary>
    public string? Default { get; set; }
}

/// <summary>Ändert den Starttyp eines Windows-Dienstes.</summary>
public sealed class ServiceAction : TweakAction
{
    public string Name { get; set; } = string.Empty;

    public ServiceStartType StartType { get; set; }

    /// <summary>Windows-Standard für Rückgängig ohne Journal-Eintrag.</summary>
    public ServiceStartType Default { get; set; } = ServiceStartType.Manual;
}

/// <summary>
/// Aktiviert einen Energieplan. Fehlt der Plan (z. B. Ultimative Leistung), wird er aus der Vorlage erzeugt
/// und beim Rückgängig wieder gelöscht. Muss die einzige Aktion des Tweaks sein.
/// </summary>
public sealed class PowerPlanAction : TweakAction
{
    /// <summary>GUID der Vorlage, z. B. e9a42b02-d5df-448d-aa00-03f14749eb61 für Ultimative Leistung.</summary>
    public string Template { get; set; } = string.Empty;
}

/// <summary>
/// Setzt eine Einstellung in der JSON-Einstellungsdatei eines Programms (z. B. Discord).
/// Nur oberste Ebene, nur in Benutzer-Ordnern. Tweaks mit dieser Aktion enthalten nur JSON-Aktionen.
/// </summary>
public sealed class JsonSettingAction : TweakAction
{
    /// <summary>Pfad mit Umgebungsvariablen, z. B. %APPDATA%\discord\settings.json.</summary>
    public string File { get; set; } = string.Empty;

    public string Property { get; set; } = string.Empty;

    public JsonElement Value { get; set; }

    /// <summary>Standard des Programms für Rückgängig ohne Journal. Fehlt er, wird die Einstellung entfernt.</summary>
    public JsonElement? Default { get; set; }

    /// <summary>Prozessname ohne .exe. Läuft das Programm, wird nichts geändert, weil es die Datei sonst überschreibt.</summary>
    public string? Process { get; set; }
}
