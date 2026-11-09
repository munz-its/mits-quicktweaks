using System.Globalization;
using System.Text.Json;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Core.Tools;

/// <summary>Systemzugriffe des Game Mode, im Windows-Projekt umgesetzt.</summary>
public interface IGameModeSystem
{
    bool IsModernStandby { get; }

    Guid GetActivePlan();

    bool PlanExists(Guid plan);

    void SetActivePlan(Guid plan);

    Guid? GetOverlay();

    void SetOverlay(Guid overlay);

    bool IsServiceRunning(string service);

    void StopService(string service);

    void StartService(string service);
}

/// <summary>Was vor dem Einschalten aktiv war. Liegt auf der Platte, damit der Game Mode auch nach Neustart oder Absturz sauber ausgeht.</summary>
public sealed class GameModeState
{
    public bool Active { get; set; }

    public DateTimeOffset Since { get; set; }

    public Guid? PreviousPlan { get; set; }

    public Guid? PreviousOverlay { get; set; }

    public List<string> StoppedServices { get; set; } = new();

    /// <summary>Vorherige Pause-Werte von Windows Update. Null-Wert = existierte nicht.</summary>
    public Dictionary<string, string?> PreviousUpdateValues { get; set; } = new();
}

public sealed record GameModeStep(string Name, bool Success, string? Error);

/// <summary>
/// Game Mode ohne Hintergrundprozess: Einschalten merkt sich den vorherigen Zustand, Ausschalten stellt ihn her.
/// Er bleibt an, bis er ausgeschaltet wird, auch über einen Neustart hinweg.
/// </summary>
public sealed class GameModeController
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    /// <summary>Suchindex und Übermittlungsoptimierung: erzeugen beim Spielen Festplatten- und Netzlast.</summary>
    public static readonly IReadOnlyList<string> BackgroundServices = new[] { "WSearch", "DoSvc" };

    public const string UpdateKey = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    /// <summary>Dieselben Werte, die "Updates für 1 Woche aussetzen" in den Einstellungen schreibt.</summary>
    public static readonly IReadOnlyList<string> UpdateValues = new[]
    {
        "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
        "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
    };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IGameModeSystem _system;
    private readonly IRegistry _registry;
    private readonly string _statePath;
    private readonly TimeProvider _time;

    public GameModeController(IGameModeSystem system, IRegistry registry, string statePath, TimeProvider? time = null)
    {
        _system = system;
        _registry = registry;
        _statePath = statePath;
        _time = time ?? TimeProvider.System;
    }

    public GameModeState State => Load();

    public bool IsActive => Load().Active;

    public IReadOnlyList<GameModeStep> TurnOn()
    {
        var state = Load();
        if (state.Active)
        {
            return Array.Empty<GameModeStep>();
        }

        state = new GameModeState { Active = true, Since = _time.GetUtcNow() };
        var steps = new List<GameModeStep>();

        // 1. Energie auf Leistung
        steps.Add(Step("Power", () =>
        {
            if (!_system.IsModernStandby && _system.PlanExists(HighPerformance))
            {
                state.PreviousPlan = _system.GetActivePlan();
                if (state.PreviousPlan != HighPerformance)
                {
                    _system.SetActivePlan(HighPerformance);
                }
            }
            else
            {
                state.PreviousOverlay = _system.GetOverlay() ?? Guid.Empty;
                _system.SetOverlay(Modules.PowerModeIds.Performance);
            }
        }));
        Save(state);

        // 2. Hintergrundlast stoppen
        foreach (var service in BackgroundServices)
        {
            steps.Add(Step("Service." + service, () =>
            {
                if (_system.IsServiceRunning(service))
                {
                    _system.StopService(service);
                    state.StoppedServices.Add(service);
                }
            }));
            Save(state);
        }

        // 3. Windows Update eine Woche pausieren, beim Ausschalten wieder freigeben
        steps.Add(Step("Update", () =>
        {
            foreach (var name in UpdateValues)
            {
                state.PreviousUpdateValues[name] = _registry.Read(RegistryHive.HKLM, UpdateKey, name)?.Data;
            }

            Save(state);
            var now = _time.GetUtcNow().UtcDateTime;
            var start = now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var end = now.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            foreach (var name in UpdateValues)
            {
                _registry.Write(RegistryHive.HKLM, UpdateKey, name, RegistryKind.String, name.Contains("Start", StringComparison.Ordinal) ? start : end);
            }
        }));
        Save(state);
        return steps;
    }

    public IReadOnlyList<GameModeStep> TurnOff()
    {
        var state = Load();
        if (!state.Active)
        {
            return Array.Empty<GameModeStep>();
        }

        var steps = new List<GameModeStep>
        {
            Step("Power", () =>
            {
                if (state.PreviousPlan is { } plan && plan != HighPerformance && _system.PlanExists(plan))
                {
                    _system.SetActivePlan(plan);
                }

                if (state.PreviousOverlay is { } overlay)
                {
                    _system.SetOverlay(overlay);
                }
            }),
        };

        foreach (var service in state.StoppedServices)
        {
            steps.Add(Step("Service." + service, () => _system.StartService(service)));
        }

        steps.Add(Step("Update", () =>
        {
            foreach (var (name, value) in state.PreviousUpdateValues)
            {
                if (value is null)
                {
                    _registry.Delete(RegistryHive.HKLM, UpdateKey, name);
                }
                else
                {
                    _registry.Write(RegistryHive.HKLM, UpdateKey, name, RegistryKind.String, value);
                }
            }
        }));

        // Auch bei Teilfehlern aus: ein hängender "an"-Zustand würde den Nutzer mehr verwirren
        Save(new GameModeState());
        return steps;
    }

    private static GameModeStep Step(string name, Action action)
    {
        try
        {
            action();
            return new GameModeStep(name, true, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or System.Security.SecurityException or NotSupportedException
                                       or System.Runtime.InteropServices.ExternalException or TimeoutException)
        {
            return new GameModeStep(name, false, ex.Message);
        }
    }

    private GameModeState Load()
    {
        try
        {
            return File.Exists(_statePath)
                ? JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(_statePath), Options) ?? new GameModeState()
                : new GameModeState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new GameModeState();
        }
    }

    private void Save(GameModeState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, Options));
    }
}
