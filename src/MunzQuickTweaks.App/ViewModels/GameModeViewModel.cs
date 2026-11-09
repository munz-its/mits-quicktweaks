using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Ein Knopf für Spiele-Sessions: Leistung an, Hintergrundlast aus, Updates pausiert. Aus stellt alles wieder her.</summary>
public sealed partial class GameModeViewModel : PageViewModel
{
    private readonly GameModeController _controller;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<GameModeViewModel> _log;

    public GameModeViewModel(GameModeController controller, HistoryStore history, LocalizationService localization, ILogger<GameModeViewModel> log)
        : base("Nav.GameMode")
    {
        _controller = controller;
        _history = history;
        _localization = localization;
        _log = log;
        localization.LanguageChanged += (_, _) => Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _since = string.Empty;

    [ObservableProperty]
    private string _result = string.Empty;

    [ObservableProperty]
    private bool _resultIsError;

    public string ButtonText => _localization.Get(IsActive ? "GameMode.TurnOff" : "GameMode.TurnOn");

    public override Task OnNavigatedToAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        var state = _controller.State;
        IsActive = state.Active;
        Since = state.Active
            ? string.Format(CultureInfo.CurrentCulture, _localization.Get("GameMode.Since"), state.Since.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : string.Empty;
        OnPropertyChanged(nameof(ButtonText));
    }

    private bool CanToggle() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        Result = _localization.Get(IsActive ? "GameMode.Stopping" : "GameMode.Starting");
        ResultIsError = false;
        try
        {
            var turningOn = !IsActive;
            var steps = await Task.Run(() => turningOn ? _controller.TurnOn() : _controller.TurnOff());
            var failed = steps.Where(s => !s.Success).ToList();
            foreach (var step in steps)
            {
                _log.LogInformation("Game Mode {State} {Step}: {Success} {Error}", turningOn ? "an" : "aus", step.Name, step.Success, step.Error);
            }

            _history.Add(HistoryAction.AppAction, turningOn ? "gamemode.on" : "gamemode.off", failed.Count == 0,
                failed.Count == 0 ? null : string.Join("; ", failed.Select(f => $"{f.Name}: {f.Error}")));

            ResultIsError = failed.Count > 0;
            Result = failed.Count == 0
                ? _localization.Get(turningOn ? "GameMode.On" : "GameMode.Off")
                : $"{_localization.Get("GameMode.Partial")} {string.Join(" · ", failed.Select(f => $"{StepName(f.Name)}: {f.Error}"))}";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private string StepName(string step) => step switch
    {
        "Power" => _localization.Get("GameMode.Step.Power"),
        "Update" => _localization.Get("GameMode.Step.Update"),
        _ => step.Replace("Service.", string.Empty, StringComparison.Ordinal),
    };
}
