using System.Reflection;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.Services;

/// <summary>
/// Verbindet Katalog, Engine und die drei Sicherungsebenen:
/// Wiederherstellungspunkt (einmal pro Sitzung), .reg-Export (vor dem ersten Anwenden), Journal (immer).
/// </summary>
public sealed class TweakService
{
    private const string CatalogPrefix = "catalog.";

    private readonly RestorePointGate _gate;
    private readonly IRegistryBackup _backup;
    private readonly HistoryStore _history;
    private readonly ILogger<TweakService> _log;
    private readonly Lazy<(IReadOnlyList<TweakDefinition> Tweaks, string? Error)> _catalog;

    public TweakService(TweakEngine engine, RestorePointGate gate, IRegistryBackup backup, HistoryStore history, ILogger<TweakService> log)
    {
        Engine = engine;
        _gate = gate;
        _backup = backup;
        _history = history;
        _log = log;
        _catalog = new Lazy<(IReadOnlyList<TweakDefinition>, string?)>(LoadCatalog);

        if (!engine.UserHiveAllowed)
        {
            _log.LogWarning("Fremdes Admin-Konto erkannt: Benutzer-Tweaks gesperrt");
        }
    }

    public TweakEngine Engine { get; }

    public IReadOnlyList<TweakDefinition> Tweaks => _catalog.Value.Tweaks;

    /// <summary>Fehlermeldung, falls der Katalog nicht geladen werden konnte.</summary>
    public string? CatalogError => _catalog.Value.Error;

    /// <summary>Wird nach jeder Änderung ausgelöst (Dashboard und Wiederherstellung aktualisieren sich).</summary>
    public event EventHandler? Changed;

    /// <summary>Ein erfolgreicher Tweak braucht Explorer-Neustart, Abmelden oder Neustart.</summary>
    public event EventHandler<RestartHint>? RestartNeeded;

    /// <summary>
    /// Tweaks, die laut Journal aktiv sein sollten, aber nicht mehr im Zielzustand sind:
    /// meist von einem Windows-Update zurückgesetzt.
    /// </summary>
    public IReadOnlyList<TweakDefinition> FindResetByWindows() =>
        Engine.Journal.All
            .Select(e => Find(e.TweakId))
            .OfType<TweakDefinition>()
            .Where(t => GetStatus(t) is TweakStatus.NotApplied or TweakStatus.Partial)
            .ToList();

    public TweakStatus GetStatus(TweakDefinition tweak)
    {
        try
        {
            return Engine.GetStatus(tweak);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Status von {TweakId} nicht lesbar", tweak.Id);
            return TweakStatus.NotAvailable;
        }
    }

    /// <summary>App läuft unter einem anderen Konto als dem angemeldeten Nutzer.</summary>
    public bool IsForeignAccount => !Engine.UserHiveAllowed;

    public TweakDefinition? Find(string id) => Tweaks.FirstOrDefault(t => t.Id == id);

    public async Task<TweakResult> ApplyAsync(TweakDefinition tweak, ISafetyPrompt prompt)
    {
        var result = await Task.Run(() =>
        {
            // Ebene 1: Wiederherstellungspunkt, einmal pro Sitzung
            if (_gate.Ensure(prompt) == GateResult.Cancelled)
            {
                _log.LogInformation("{TweakId}: vom Nutzer abgebrochen", tweak.Id);
                return TweakResult.Fail("Abgebrochen.");
            }

            // Ebene 3: .reg-Export, nur beim ersten Anwenden (danach wäre es nicht mehr der Originalzustand)
            if (Engine.Journal.Get(tweak.Id) is null)
            {
                prompt.ReportProgress("backup");
                try
                {
                    var file = _backup.Export(tweak.Id, Engine.AffectedKeys(tweak));
                    if (file is not null)
                    {
                        _history.Add(HistoryAction.RegistryExported, tweak.Id, true, Path.GetFileName(file));
                        _log.LogInformation("{TweakId}: Registry exportiert nach {File}", tweak.Id, file);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Export ist Zusatzschutz: Journal sichert trotzdem jeden Wert
                    _log.LogWarning(ex, "{TweakId}: Registry-Export fehlgeschlagen", tweak.Id);
                    _history.Add(HistoryAction.RegistryExported, tweak.Id, false, ex.Message);
                }
            }

            // Ebene 2: Journal, in der Engine
            prompt.ReportProgress("apply");
            return Engine.Apply(tweak);
        });

        Record(HistoryAction.Applied, tweak, result);
        return result;
    }

    public async Task<TweakResult> RevertAsync(TweakDefinition tweak)
    {
        var result = await Task.Run(() => Engine.Revert(tweak));
        Record(HistoryAction.Reverted, tweak, result);
        return result;
    }

    /// <summary>Setzt alle Tweaks zurück, die laut Journal aktiv sind. Gibt die Anzahl der Fehler zurück.</summary>
    public async Task<int> RevertAllAsync()
    {
        var errors = 0;
        foreach (var entry in Engine.Journal.All)
        {
            var tweak = Find(entry.TweakId);
            if (tweak is null)
            {
                // Tweak aus älterer Version, nicht mehr im Katalog: Journal bleibt erhalten
                _log.LogWarning("{TweakId}: nicht im Katalog, kann nicht zurückgesetzt werden", entry.TweakId);
                errors++;
                continue;
            }

            var result = await RevertAsync(tweak);
            if (!result.Success)
            {
                errors++;
            }
        }

        return errors;
    }

    /// <summary>Anzahl der Tweaks, die gerade wirklich aktiv sind.</summary>
    public int CountApplied() => Tweaks.Count(t => GetStatus(t) == TweakStatus.Applied);

    private void Record(HistoryAction action, TweakDefinition tweak, TweakResult result)
    {
        _history.Add(action, tweak.Id, result.Success, result.Error);
        if (result.Success)
        {
            _log.LogInformation("{TweakId}: {Action}", tweak.Id, action);
            if (tweak.Restart != RestartHint.None)
            {
                RestartNeeded?.Invoke(this, tweak.Restart);
            }
        }
        else
        {
            _log.LogWarning("{TweakId}: {Action} fehlgeschlagen: {Error}", tweak.Id, action, result.Error);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private (IReadOnlyList<TweakDefinition>, string?) LoadCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var streams = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(CatalogPrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => (n, assembly.GetManifestResourceStream(n)!))
            .ToList();

        try
        {
            var tweaks = CatalogLoader.Load(streams);
            _log.LogInformation("Katalog geladen: {Count} Tweaks", tweaks.Count);
            return (tweaks, null);
        }
        catch (CatalogException ex)
        {
            _log.LogError(ex, "Tweak-Katalog ungültig");
            return (Array.Empty<TweakDefinition>(), ex.Message);
        }
        finally
        {
            foreach (var (_, stream) in streams)
            {
                stream.Dispose();
            }
        }
    }
}
