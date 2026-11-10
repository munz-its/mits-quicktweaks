using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Core.Modules;

/// <summary>Werte wie unter Einstellungen > System > Anzeige > Grafik.</summary>
public enum GpuChoice
{
    Auto = 0,
    PowerSaving = 1,
    HighPerformance = 2,
}

public sealed record GpuRule(string ExePath, GpuChoice Choice);

/// <summary>
/// Grafikkarte pro Programm, derselbe Speicherort wie die Windows-Grafikeinstellungen.
/// Andere Teile des Werts (z. B. SwapEffectUpgradeEnable) bleiben erhalten.
/// </summary>
public sealed class GpuPreferences
{
    public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string Field = "GpuPreference";

    private readonly IRegistry _registry;

    public GpuPreferences(IRegistry registry)
    {
        _registry = registry;
    }

    public IReadOnlyList<GpuRule> List() =>
        _registry.ValueNames(RegistryHive.HKCU, Key)
            .Where(name => name.Contains('\\', StringComparison.Ordinal))
            .Select(name => (name, choice: Parse(_registry.Read(RegistryHive.HKCU, Key, name)?.Data)))
            .Where(x => x.choice is not null and not GpuChoice.Auto)
            .Select(x => new GpuRule(x.name, x.choice!.Value))
            .OrderBy(r => Path.GetFileName(r.ExePath), StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Rohwert vorher, für das Änderungsbuch. Null = gab es nicht.</summary>
    public string? RawValue(string exePath) => _registry.Read(RegistryHive.HKCU, Key, exePath)?.Data;

    public void Set(string exePath, GpuChoice choice)
    {
        Validate(exePath);
        _registry.Write(RegistryHive.HKCU, Key, exePath, RegistryKind.String, Merge(RawValue(exePath), choice));
    }

    /// <summary>Entfernt nur die Grafikkarten-Wahl. Bleibt sonst nichts übrig, verschwindet der Wert ganz.</summary>
    public void Remove(string exePath)
    {
        Validate(exePath);
        var rest = Merge(RawValue(exePath), null);
        if (rest.Length == 0)
        {
            _registry.Delete(RegistryHive.HKCU, Key, exePath);
        }
        else
        {
            _registry.Write(RegistryHive.HKCU, Key, exePath, RegistryKind.String, rest);
        }
    }

    /// <summary>Stellt einen gemerkten Rohwert wieder her.</summary>
    public void Restore(string exePath, string? raw)
    {
        if (raw is null)
        {
            _registry.Delete(RegistryHive.HKCU, Key, exePath);
        }
        else
        {
            _registry.Write(RegistryHive.HKCU, Key, exePath, RegistryKind.String, raw);
        }
    }

    internal static GpuChoice? Parse(string? data)
    {
        foreach (var part in (data ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(Field, StringComparison.OrdinalIgnoreCase) && int.TryParse(pair[1], out var n) && Enum.IsDefined(typeof(GpuChoice), n))
            {
                return (GpuChoice)n;
            }
        }

        return null;
    }

    internal static string Merge(string? data, GpuChoice? choice)
    {
        var parts = (data ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith(Field + "=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (choice is { } c)
        {
            parts.Insert(0, $"{Field}={(int)c}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(";", parts) + ";";
    }

    private static void Validate(string exePath)
    {
        if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exePath.Length < 7 || exePath[1] != ':' || exePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Bitte ein Programm mit vollständigem Pfad wählen.", nameof(exePath));
        }
    }
}
