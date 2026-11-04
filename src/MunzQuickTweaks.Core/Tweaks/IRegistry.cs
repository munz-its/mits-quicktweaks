namespace MunzQuickTweaks.Core.Tweaks;

/// <summary>Ein gelesener Registry-Wert, Inhalt immer als Text (DWord als vorzeichenlose Zahl).</summary>
public sealed record RegistryValue(RegistryKind Kind, string Data);

/// <summary>
/// Registry-Zugriff als Schnittstelle: die Engine bleibt plattformunabhängig
/// und lässt sich mit einer Test-Registry vollständig prüfen.
/// </summary>
public interface IRegistry
{
    bool KeyExists(RegistryHive hive, string key);

    RegistryValue? Read(RegistryHive hive, string key, string name);

    /// <summary>Legt fehlende Schlüssel automatisch an.</summary>
    void Write(RegistryHive hive, string key, string name, RegistryKind kind, string data);

    void Delete(RegistryHive hive, string key, string name);

    /// <summary>Löscht einen Schlüssel nur, wenn er weder Werte noch Unterschlüssel hat.</summary>
    void DeleteKeyIfEmpty(RegistryHive hive, string key);
}

/// <summary>Benachrichtigt Windows nach Änderungen, damit sie ohne Neustart greifen.</summary>
public interface ISystemNotifier
{
    void RefreshExplorer();
}

/// <summary>Energiepläne über die Windows-Energie-API.</summary>
public interface IPowerPlans
{
    /// <summary>False auf Geräten mit Modern Standby: dort gibt es nur den Plan "Ausbalanciert".</summary>
    bool IsSupported { get; }

    Guid GetActive();

    bool Exists(Guid scheme);

    /// <summary>Erzeugt eine Kopie der Vorlage und gibt die neue GUID zurück.</summary>
    Guid Duplicate(Guid template);

    void SetActive(Guid scheme);

    void Delete(Guid scheme);
}
