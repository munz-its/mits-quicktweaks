using System.Globalization;

namespace MunzQuickTweaks.Core.Tweaks;

public enum TweakStatus
{
    /// <summary>Passt nicht zu dieser Windows-Version oder die Funktion fehlt (z. B. Dienst nicht vorhanden).</summary>
    NotAvailable,
    NotApplied,
    /// <summary>Nur ein Teil der Aktionen ist im Zielzustand, z. B. nach einem Windows-Update.</summary>
    Partial,
    Applied,
}

public sealed record TweakResult(bool Success, string? Error = null)
{
    public static TweakResult Ok { get; } = new(true);

    public static TweakResult Fail(string error) => new(false, error);
}

/// <summary>
/// Führt Tweaks in 5 Schritten aus: Prüfen, Sichern, Anwenden, Kontrollieren, Protokollieren.
/// Schlägt ein Schritt fehl, wird alles bereits Geänderte automatisch zurückgesetzt.
/// </summary>
public sealed class TweakEngine
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";

    private readonly IRegistry _registry;
    private readonly JournalStore _journal;
    private readonly ISystemNotifier _notifier;
    private readonly int _build;
    private readonly TimeProvider _time;

    public TweakEngine(IRegistry registry, JournalStore journal, ISystemNotifier notifier, int build, TimeProvider? time = null)
    {
        _registry = registry;
        _journal = journal;
        _notifier = notifier;
        _build = build;
        _time = time ?? TimeProvider.System;
    }

    public JournalStore Journal => _journal;

    public TweakStatus GetStatus(TweakDefinition tweak)
    {
        if (!IsAvailable(tweak))
        {
            return TweakStatus.NotAvailable;
        }

        var steps = Expand(tweak).ToList();
        var matching = steps.Count(IsInTargetState);
        return matching == steps.Count ? TweakStatus.Applied
            : matching == 0 ? TweakStatus.NotApplied
            : TweakStatus.Partial;
    }

    public TweakResult Apply(TweakDefinition tweak)
    {
        if (!IsAvailable(tweak))
        {
            return TweakResult.Fail("Auf diesem System nicht verfügbar.");
        }

        var steps = Expand(tweak).ToList();

        var existing = _journal.Get(tweak.Id);
        List<RegistrySnapshot> snapshots;
        List<RegistrySnapshot> thisRun;
        try
        {
            // Beim erneuten Anwenden bleibt der ursprüngliche Zustand erhalten, nie den Zwischenstand sichern
            thisRun = steps.Select(TakeSnapshot).ToList();
            snapshots = existing?.Snapshots.OfType<RegistrySnapshot>().ToList() ?? thisRun;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return TweakResult.Fail(ex.Message);
        }

        try
        {
            foreach (var step in steps)
            {
                _registry.Write(step.Hive, step.Key, step.Name, step.Kind, step.Value);
            }

            var failed = steps.FirstOrDefault(s => !IsInTargetState(s));
            if (failed is not null)
            {
                Restore(thisRun);
                return TweakResult.Fail($"Kontrolle fehlgeschlagen: {failed.Key}\\{failed.Name}");
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Restore(thisRun);
            return TweakResult.Fail(ex.Message);
        }

        _journal.Set(new JournalEntry
        {
            TweakId = tweak.Id,
            AppliedAt = existing?.AppliedAt ?? _time.GetUtcNow(),
            Snapshots = snapshots.Cast<ActionSnapshot>().ToList(),
        });

        Notify(tweak);
        return TweakResult.Ok;
    }

    public TweakResult Revert(TweakDefinition tweak)
    {
        var entry = _journal.Get(tweak.Id);
        var steps = Expand(tweak).ToList();
        List<RegistrySnapshot> beforeRevert;
        try
        {
            beforeRevert = steps.Select(TakeSnapshot).ToList();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return TweakResult.Fail(ex.Message);
        }

        try
        {
            if (entry is not null)
            {
                // Exakter Originalzustand aus dem Journal
                Restore(entry.Snapshots.OfType<RegistrySnapshot>().ToList());
            }
            else
            {
                // Kein Journal (z. B. außerhalb der App gesetzt): Windows-Standard herstellen
                foreach (var step in steps)
                {
                    if (step.Default is null)
                    {
                        _registry.Delete(step.Hive, step.Key, step.Name);
                    }
                    else
                    {
                        _registry.Write(step.Hive, step.Key, step.Name, step.Kind, step.Default);
                    }
                }
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Restore(beforeRevert);
            return TweakResult.Fail(ex.Message);
        }

        _journal.Remove(tweak.Id);
        Notify(tweak);
        return TweakResult.Ok;
    }

    /// <summary>Alle Registry-Schlüssel, die der Tweak berührt (für den .reg-Export vor dem Anwenden).</summary>
    public IReadOnlyCollection<(RegistryHive Hive, string Key)> AffectedKeys(TweakDefinition tweak) =>
        Expand(tweak).Select(s => (s.Hive, s.Key)).Distinct().ToList();

    private static bool IsExpected(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or NotSupportedException;

    private bool IsAvailable(TweakDefinition tweak)
    {
        if (!tweak.SupportsBuild(_build))
        {
            return false;
        }

        // Dienst-Tweaks nur, wenn der Dienst auf diesem PC existiert
        return tweak.Actions.OfType<ServiceAction>()
            .All(s => _registry.KeyExists(RegistryHive.HKLM, ServicesKey + s.Name));
    }

    private void Notify(TweakDefinition tweak)
    {
        if (tweak.Restart == RestartHint.Explorer)
        {
            _notifier.RefreshExplorer();
        }
    }

    private RegistrySnapshot TakeSnapshot(Step step) => new()
    {
        Hive = step.Hive,
        Key = step.Key,
        Name = step.Name,
        KeyExisted = _registry.KeyExists(step.Hive, step.Key),
        Previous = _registry.Read(step.Hive, step.Key, step.Name),
    };

    private void Restore(IReadOnlyList<RegistrySnapshot> snapshots)
    {
        // Rückwärts, damit die Reihenfolge der Änderungen gespiegelt wird
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            var s = snapshots[i];
            if (s.Previous is null)
            {
                _registry.Delete(s.Hive, s.Key, s.Name);
                if (!s.KeyExisted)
                {
                    _registry.DeleteKeyIfEmpty(s.Hive, s.Key);
                }
            }
            else
            {
                _registry.Write(s.Hive, s.Key, s.Name, s.Previous.Kind, s.Previous.Data);
            }
        }
    }

    private bool IsInTargetState(Step step)
    {
        try
        {
            var current = _registry.Read(step.Hive, step.Key, step.Name);
            return current is not null && ValuesEqual(step.Kind, current, step.Value);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    internal static bool ValuesEqual(RegistryKind kind, RegistryValue current, string target)
    {
        if (kind is RegistryKind.DWord or RegistryKind.QWord)
        {
            return current.Kind is RegistryKind.DWord or RegistryKind.QWord
                && ulong.TryParse(current.Data, NumberStyles.None, CultureInfo.InvariantCulture, out var a)
                && ulong.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var b)
                && a == b;
        }

        return current.Kind is RegistryKind.String or RegistryKind.ExpandString
            && string.Equals(current.Data, target, StringComparison.Ordinal);
    }

    /// <summary>Alle Aktionen als einzelne Registry-Schritte. Dienste werden über ihren Start-Wert geändert.</summary>
    private static IEnumerable<Step> Expand(TweakDefinition tweak)
    {
        foreach (var action in tweak.Actions)
        {
            switch (action)
            {
                case RegistryAction r:
                    yield return new Step(r.Hive, r.Key, r.Name, r.Kind, r.Value, r.Default);
                    break;

                case ServiceAction s:
                    var key = ServicesKey + s.Name;
                    yield return new Step(RegistryHive.HKLM, key, "Start", RegistryKind.DWord, StartValue(s.StartType), StartValue(s.Default));
                    yield return new Step(RegistryHive.HKLM, key, "DelayedAutostart", RegistryKind.DWord,
                        s.StartType == ServiceStartType.AutomaticDelayed ? "1" : "0",
                        s.Default == ServiceStartType.AutomaticDelayed ? "1" : "0");
                    break;

                default:
                    throw new NotSupportedException($"Unbekannte Aktion: {action.GetType().Name}");
            }
        }
    }

    private static string StartValue(ServiceStartType type) => type switch
    {
        ServiceStartType.Automatic or ServiceStartType.AutomaticDelayed => "2",
        ServiceStartType.Manual => "3",
        ServiceStartType.Disabled => "4",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private sealed record Step(RegistryHive Hive, string Key, string Name, RegistryKind Kind, string Value, string? Default);
}
