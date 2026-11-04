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
