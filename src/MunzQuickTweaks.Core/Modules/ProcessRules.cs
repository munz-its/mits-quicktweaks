using System.Globalization;
using System.Text.RegularExpressions;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Core.Modules;

/// <summary>Werte wie im Windows-Schlüssel PerfOptions. Echtzeit (4) gibt es bewusst nicht: sie kann Windows einfrieren.</summary>
public enum CpuPriority
{
    Idle = 1,
    Normal = 2,
    High = 3,
    BelowNormal = 5,
    AboveNormal = 6,
}

/// <summary>Datenträger-Priorität. Hoch nimmt Windows über diesen Weg nicht an, daher nur Niedrig und Normal.</summary>
public enum IoPriority
{
    Low = 1,
    Normal = 2,
}

public sealed record ProcessRule(string Exe, CpuPriority? Cpu, IoPriority? Io);

/// <summary>
/// Feste Priorität für ein Programm bei jedem Start, über Image File Execution Options\PerfOptions.
/// Die App fasst dort nur CpuPriorityClass und IoPriority an, nie andere Werte wie Debugger.
/// </summary>
public sealed partial class ProcessRules
{
    public const string BaseKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    private const string CpuValue = "CpuPriorityClass";
    private const string IoValue = "IoPriority";

    /// <summary>Systemprozesse: eine falsche Priorität kann Anmeldung, Sicherheit oder Bildschirm lahmlegen.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss.exe", "smss.exe", "wininit.exe", "winlogon.exe", "lsass.exe", "lsaiso.exe", "services.exe", "svchost.exe",
        "dwm.exe", "explorer.exe", "sihost.exe", "fontdrvhost.exe", "ntoskrnl.exe", "System", "Registry", "MsMpEng.exe",
        "SecurityHealthService.exe", "ctfmon.exe", "conhost.exe", "taskhostw.exe", "RuntimeBroker.exe", "MunzQuickTweaks.exe",
    };

    private readonly IRegistry _registry;

    public ProcessRules(IRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Fehlertext oder null, wenn der Name passt.</summary>
    public static string? Validate(string exe)
    {
        if (!ExePattern().IsMatch(exe))
        {
            return "Bitte nur den Dateinamen angeben, z. B. spiel.exe.";
        }

        return Protected.Contains(exe) ? $"{exe} ist ein Windows-Systemprozess und bleibt unverändert." : null;
    }

    public IReadOnlyList<ProcessRule> List()
    {
        var rules = new List<ProcessRule>();
        foreach (var exe in _registry.SubKeys(RegistryHive.HKLM, BaseKey))
        {
            var key = PerfKey(exe);
            var cpu = ReadNumber(key, CpuValue);
            var io = ReadNumber(key, IoValue);
            if (cpu is null && io is null)
            {
                continue;
            }

            rules.Add(new ProcessRule(
                exe,
                cpu is { } c && Enum.IsDefined(typeof(CpuPriority), (int)c) ? (CpuPriority)c : null,
                io is { } i && Enum.IsDefined(typeof(IoPriority), (int)i) ? (IoPriority)i : null));
        }

        return rules.OrderBy(r => r.Exe, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Set(ProcessRule rule)
    {
        if (Validate(rule.Exe) is { } error)
        {
            throw new ArgumentException(error, nameof(rule));
        }

        var key = PerfKey(rule.Exe);
        WriteOrDelete(key, CpuValue, rule.Cpu is { } cpu ? (int)cpu : null);
        WriteOrDelete(key, IoValue, rule.Io is { } io ? (int)io : null);
        CleanUp(rule.Exe);
    }

    public void Remove(string exe)
    {
        if (Validate(exe) is { } error)
        {
            throw new ArgumentException(error, nameof(exe));
        }

        var key = PerfKey(exe);
        _registry.Delete(RegistryHive.HKLM, key, CpuValue);
        _registry.Delete(RegistryHive.HKLM, key, IoValue);
        CleanUp(exe);
    }

    private static string PerfKey(string exe) => $@"{BaseKey}\{exe}\PerfOptions";

    /// <summary>Leere Schlüssel entfernen, damit nach dem Löschen nichts zurückbleibt.</summary>
    private void CleanUp(string exe)
    {
        _registry.DeleteKeyIfEmpty(RegistryHive.HKLM, PerfKey(exe));
        _registry.DeleteKeyIfEmpty(RegistryHive.HKLM, $@"{BaseKey}\{exe}");
    }

    private void WriteOrDelete(string key, string name, int? value)
    {
        if (value is null)
        {
            _registry.Delete(RegistryHive.HKLM, key, name);
        }
        else
        {
            _registry.Write(RegistryHive.HKLM, key, name, RegistryKind.DWord, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private uint? ReadNumber(string key, string name)
    {
        try
        {
            var value = _registry.Read(RegistryHive.HKLM, key, name);
            return value is { Kind: RegistryKind.DWord } && uint.TryParse(value.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9 _.()+\-]{1,80}\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ExePattern();
}
