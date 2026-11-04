using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Core.Safety;

/// <summary>Export betroffener Registry-Schlüssel als .reg (Ebene 3, manuelle Wiederherstellung ohne App).</summary>
public interface IRegistryBackup
{
    /// <summary>Exportiert alle genannten Schlüssel in eine .reg-Datei. Gibt den Pfad zurück oder null, wenn kein Schlüssel existierte.</summary>
    string? Export(string tweakId, IReadOnlyCollection<(RegistryHive Hive, string Key)> keys);
}
