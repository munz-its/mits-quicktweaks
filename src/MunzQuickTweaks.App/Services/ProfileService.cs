using System.Globalization;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Dialogs;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tools;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.Services;

/// <summary>
/// Voreinstellungen anwenden und "Alles zurücksetzen". Vor jeder Änderung zeigt eine Vorschau alle Punkte,
/// jeder Bereich (Tweaks, Energie, Autostart, Apps) ist einzeln abwählbar.
/// </summary>
public sealed class ProfileService
{
    private readonly TweakService _tweaks;
    private readonly BatchApplyService _batch;
    private readonly IPowerModes _power;
    private readonly IStartupManager _startup;
    private readonly IAppxManager _appx;
    private readonly ChangeLedger _ledger;
    private readonly ProcessRules _processes;
    private readonly GameModeController _gameMode;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<ProfileService> _log;

    public ProfileService(TweakService tweaks, BatchApplyService batch, IPowerModes power, IStartupManager startup, IAppxManager appx,
        ChangeLedger ledger, IRegistry registry, GameModeController gameMode, HistoryStore history, LocalizationService localization, ILogger<ProfileService> log)
    {
        _tweaks = tweaks;
        _batch = batch;
        _power = power;
        _startup = startup;
        _appx = appx;
        _ledger = ledger;
        _processes = new ProcessRules(registry);
        _gameMode = gameMode;
        _history = history;
        _localization = localization;
        _log = log;
    }

    private string T(string key) => _localization.Get(key);

    private string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Wendet ein Profil nach Vorschau an. Rückgabe: Statuszeile für die Oberfläche.</summary>
    public async Task<string> RunProfileAsync(ProfileKind kind, Action<string> progress)
    {
        if (kind == ProfileKind.ResetAll)
        {
            return await ResetAllAsync(progress);
        }

        var profile = ProfileCatalog.Get(kind, _tweaks.Tweaks);
        var name = T($"Profile.{kind}");
        var lang = _localization.CurrentLanguage;

        var (todo, startup, apps, modeNeeded) = await Task.Run(() =>
        {
            var tweaks = _batch.FilterTodoAsync(profile.TweakIds).GetAwaiter().GetResult();
            var entries = SafeStartupList();
            var startupTodo = profile.StartupApps
                .Select(id => AppCatalog.All.First(a => a.Id == id))
                .SelectMany(app => AppCatalog.MatchStartup(app, entries).Where(e => e.Enabled).Select(e => (App: app, Entry: e)))
                .ToList();
            var installed = SafeInstalled();
            var appTodo = DebloatCatalog.Match(installed).Where(m => profile.Debloat.Contains(m.App.Group)).ToList();
            var mode = profile.Mode is { } m && SafeGetMode() != m;
            return (tweaks, startupTodo, appTodo, mode);
        });

        var sections = new List<PreviewSection>
        {
            new("tweaks", T("Preview.Tweaks"), null,
                todo.Select(t => new PreviewItem(t.Title.Get(lang), t, t.Risk == TweakRisk.Medium ? T("Profile.MediumMark") : null))),
        };

        if (modeNeeded && profile.Mode is { } mode)
        {
            sections.Add(new PreviewSection("power", T("Preview.Power"), T("Preview.PowerHint"),
                new[] { new PreviewItem(T($"Power.{mode}"), mode) }));
        }

        sections.Add(new PreviewSection("startup", T("Preview.Startup"), T("Preview.StartupHint"),
            startup.Select(s => new PreviewItem($"{s.App.Name} ({s.Entry.Name})", s.Entry))));
        sections.Add(new PreviewSection("apps", T("Preview.Apps"), T("Preview.AppsHint"),
            apps.Select(a => new PreviewItem(a.App.Name, new DebloatMatch(a.App, a.Packages)))));

        var model = new PreviewModel(F("Preview.ProfileHeading", name), T($"Profile.{kind}.Long"), T("Preview.Apply"), T("Preview.Cancel"), sections);
        if (model.Sections.Count == 0)
        {
            return F("Profile.AllActive", name);
        }

        if (!PreviewWindow.Ask(model))
        {
            return string.Empty;
        }

        var failed = 0;
        var done = 0;

        var chosenTweaks = model.Section("tweaks")?.Selected<TweakDefinition>().ToList() ?? new List<TweakDefinition>();
        if (chosenTweaks.Count > 0)
        {
            var result = await _batch.ApplyAsync(chosenTweaks, progress, kind.ToString());
            done += result.Applied;
            failed += result.Failed;
        }

        foreach (var chosenMode in model.Section("power")?.Selected<PowerMode>() ?? Enumerable.Empty<PowerMode>())
        {
            progress(T("Preview.Power"));
            if (await Task.Run(() => SetMode(chosenMode))) done++; else failed++;
        }

        foreach (var entry in model.Section("startup")?.Selected<StartupEntry>() ?? Enumerable.Empty<StartupEntry>())
        {
            progress($"{T("Preview.Startup")}: {entry.Name}");
            var result = await Task.Run(() => _startup.SetEnabled(entry, false));
            if (result.Success)
            {
                _ledger.RecordStartup(entry.Id, entry.Enabled);
                done++;
            }
            else
            {
                failed++;
            }

            _history.Add(HistoryAction.StartupDisabled, null, result.Success, result.Success ? entry.Name : $"{entry.Name}: {result.Error}");
        }

        foreach (var app in model.Section("apps")?.Selected<DebloatMatch>() ?? Enumerable.Empty<DebloatMatch>())
        {
            progress($"{T("Preview.Apps")}: {app.App.Name}");
            var allRemoved = true;
            string? error = null;
            foreach (var package in app.Packages)
            {
                var result = await _appx.RemoveAsync(package.FullName);
                allRemoved &= result.Success;
                error ??= result.Error;
            }

            if (allRemoved)
            {
                _ledger.RecordAppRemoved(app.App.Package);
                done++;
            }
            else
            {
                failed++;
            }

            _history.Add(HistoryAction.AppRemoved, app.App.Package, allRemoved, error);
        }

        _log.LogInformation("Profil {Kind}: {Done} erledigt, {Failed} Fehler", kind, done, failed);
        return F("Profile.Done", name, done, failed);
    }

    /// <summary>Alles zurück, was die App je geändert hat: Tweaks, Energie, Autostart, Apps, Prozessregeln, Game Mode, OneDrive.</summary>
    public async Task<string> ResetAllAsync(Action<string> progress)
    {
        var lang = _localization.CurrentLanguage;
        var ledger = _ledger.Data;

        var (tweaks, startup, apps, processes) = await Task.Run(() =>
        {
            var active = _tweaks.Engine.Journal.All.Select(e => _tweaks.Find(e.TweakId)).OfType<TweakDefinition>().ToList();
            var entries = SafeStartupList();
            var startupTodo = entries.Where(e => ledger.Startup.TryGetValue(e.Id, out var original) && original != e.Enabled).ToList();

            // Auch Apps, die vor dem Änderungsbuch über die App entfernt wurden (Protokoll)
            var removed = ledger.RemovedApps
                .Concat(_history.Latest(1000).Where(h => h.Action == HistoryAction.AppRemoved && h.Success && h.TweakId is not null).Select(h => h.TweakId!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<RestorablePackage> restorable;
            try
            {
                restorable = _appx.ListRestorable();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Wiederherstellbare Apps nicht lesbar");
                restorable = Array.Empty<RestorablePackage>();
            }

            var appTodo = restorable.Where(r => removed.Contains(r.Name)).ToList();
            var processTodo = ledger.Processes.ToList();
            return (active, startupTodo, appTodo, processTodo);
        });

        var sections = new List<PreviewSection>
        {
            new("tweaks", T("Preview.Tweaks"), null, tweaks.Select(t => new PreviewItem(t.Title.Get(lang), t))),
        };

        if (_gameMode.IsActive)
        {
            sections.Add(new PreviewSection("gamemode", T("Nav.GameMode"), null, new[] { new PreviewItem(T("GameMode.TurnOff"), "gamemode") }));
        }

        if (ledger.OriginalPlan is not null || ledger.OriginalOverlay is not null)
        {
            sections.Add(new PreviewSection("power", T("Preview.Power"), null, new[] { new PreviewItem(T("Preview.PowerRestore"), "power") }));
        }

        sections.Add(new PreviewSection("startup", T("Preview.StartupRestore"), null,
            startup.Select(e => new PreviewItem(e.Name + (ledger.Startup[e.Id] ? $" ({T("Preview.On")})" : $" ({T("Preview.Off")})"), e))));
        sections.Add(new PreviewSection("apps", T("Preview.AppsRestore"), T("Preview.AppsRestoreHint"),
            apps.Select(a => new PreviewItem(DebloatCatalog.All.FirstOrDefault(d => string.Equals(d.Package, a.Name, StringComparison.OrdinalIgnoreCase))?.Name ?? a.Name, a))));
        sections.Add(new PreviewSection("processes", T("Nav.Processes"), null,
            processes.Select(p => new PreviewItem(p.Key, p))));

        if (ledger.OneDriveUninstalled)
        {
            sections.Add(new PreviewSection("onedrive", "OneDrive", T("Preview.OneDriveHint"), new[] { new PreviewItem(T("Apps.OneDrive.Install"), "onedrive") }));
        }

        var model = new PreviewModel(T("Preview.ResetHeading"), T("Preview.ResetIntro"), T("Preview.Reset"), T("Preview.Cancel"), sections);
        if (model.Sections.Count == 0)
        {
            return T("Preview.NothingToReset");
        }

        if (!PreviewWindow.Ask(model))
        {
            return string.Empty;
        }

        var failed = 0;
        var done = 0;
        void Count(bool ok)
        {
            if (ok) done++; else failed++;
        }

        if (model.Section("gamemode")?.Selected<string>().Any() == true)
        {
            progress(T("GameMode.Stopping"));
            var steps = await Task.Run(_gameMode.TurnOff);
            Count(steps.All(s => s.Success));
        }

        foreach (var tweak in model.Section("tweaks")?.Selected<TweakDefinition>() ?? Enumerable.Empty<TweakDefinition>())
        {
            progress(tweak.Title.Get(lang));
            Count((await _tweaks.RevertAsync(tweak)).Success);
        }

        var powerDone = false;
        if (model.Section("power")?.Selected<string>().Any() == true)
        {
            progress(T("Preview.Power"));
            powerDone = await Task.Run(() => RestorePower(ledger));
            Count(powerDone);
        }

        var startupDone = true;
        foreach (var entry in model.Section("startup")?.Selected<StartupEntry>() ?? Enumerable.Empty<StartupEntry>())
        {
            progress($"{T("Preview.Startup")}: {entry.Name}");
            var original = ledger.Startup[entry.Id];
            var result = await Task.Run(() => _startup.SetEnabled(entry, original));
            _history.Add(original ? HistoryAction.StartupEnabled : HistoryAction.StartupDisabled, null, result.Success, entry.Name);
            startupDone &= result.Success;
            Count(result.Success);
        }

        var appsDone = true;
        foreach (var app in model.Section("apps")?.Selected<RestorablePackage>() ?? Enumerable.Empty<RestorablePackage>())
        {
            progress($"{T("Preview.AppsRestore")}: {app.Name}");
            var result = await _appx.ReinstallAsync(app.FamilyName);
            _history.Add(HistoryAction.AppReinstalled, app.FamilyName, result.Success, result.Error);
            if (result.Success)
            {
                _ledger.RecordAppReinstalled(app.Name);
            }

            appsDone &= result.Success;
            Count(result.Success);
        }

        var processesDone = true;
        foreach (var rule in model.Section("processes")?.Selected<KeyValuePair<string, ProcessOriginal>>() ?? Enumerable.Empty<KeyValuePair<string, ProcessOriginal>>())
        {
            try
            {
                var original = rule.Value;
                if (original.Cpu is null && original.Io is null)
                {
                    _processes.Remove(rule.Key);
                }
                else
                {
                    _processes.Set(new ProcessRule(rule.Key, (CpuPriority?)original.Cpu, (IoPriority?)original.Io));
                }

                _history.Add(HistoryAction.ProcessRuleRemoved, rule.Key, true, null);
                Count(true);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException)
            {
                _history.Add(HistoryAction.ProcessRuleRemoved, rule.Key, false, ex.Message);
                processesDone = false;
                Count(false);
            }
        }

        var oneDriveDone = false;
        if (model.Section("onedrive")?.Selected<string>().Any() == true)
        {
            Windows.OneDriveActions.StartInstall();
            _history.Add(HistoryAction.AppAction, "onedrive.install", true, null);
            oneDriveDone = true;
            Count(true);
        }

        // Nur vollständig erledigte und gewählte Bereiche aus dem Änderungsbuch streichen
        _ledger.Clear(
            power: powerDone,
            startup: startupDone && model.Section("startup")?.IsEnabled != false && startup.Count == (model.Section("startup")?.Selected<StartupEntry>().Count() ?? 0),
            apps: false,
            processes: processesDone && model.Section("processes") is { } ps && ps.Selected<KeyValuePair<string, ProcessOriginal>>().Count() == processes.Count,
            oneDrive: oneDriveDone);

        _log.LogInformation("Alles zurücksetzen: {Done} erledigt, {Failed} Fehler", done, failed);
        return failed == 0 ? T("Preview.ResetDone") : F("Preview.ResetErrors", failed);
    }

    private bool SetMode(PowerMode mode)
    {
        try
        {
            RecordPowerOriginal();
            if (!_power.ModeAvailable)
            {
                // Bei Beste Leistung reicht der aktive Leistungsplan. Für Effizienz braucht Windows den Plan Ausbalanciert.
                if (mode != PowerMode.Efficiency)
                {
                    return true;
                }

                _power.ActivatePlan(TweakEngine.BalancedScheme);
            }

            _power.SetMode(mode);
            _history.Add(HistoryAction.PowerModeChanged, $"mode.{mode}", true, null);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _history.Add(HistoryAction.PowerModeChanged, $"mode.{mode}", false, ex.Message);
            return false;
        }
    }

    private void RecordPowerOriginal()
    {
        var plan = _power.ListPlans().FirstOrDefault(p => p.IsActive)?.Id;
        if (plan is { } p)
        {
            _ledger.RecordPower(p, Windows.WindowsPowerModes.GetOverlayId());
        }
    }

    private bool RestorePower(LedgerData ledger)
    {
        try
        {
            if (ledger.OriginalPlan is { } plan && _power.ListPlans().Any(p => p.Id == plan))
            {
                _power.ActivatePlan(plan);
            }

            if (ledger.OriginalOverlay is { } overlay)
            {
                Windows.WindowsPowerModes.SetOverlayId(overlay);
            }

            _history.Add(HistoryAction.PowerModeChanged, "restore", true, null);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _history.Add(HistoryAction.PowerModeChanged, "restore", false, ex.Message);
            return false;
        }
    }

    private PowerMode? SafeGetMode()
    {
        try
        {
            return _power.ModeAvailable ? _power.GetMode() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private IReadOnlyList<StartupEntry> SafeStartupList()
    {
        try
        {
            return _startup.List();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return Array.Empty<StartupEntry>();
        }
    }

    private IReadOnlyList<InstalledPackage> SafeInstalled()
    {
        try
        {
            return _appx.ListInstalled();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Installierte Apps nicht lesbar");
            return Array.Empty<InstalledPackage>();
        }
    }

    /// <summary>Für andere Seiten: Ursprungszustand vor einer Energie-Änderung merken.</summary>
    public void RememberPower() => RecordPowerOriginal();
}
