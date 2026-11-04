using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Eine Tweak-Karte: Titel, Erklärung, Risiko, Schalter und Rückmeldung.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly TweakService _service;
    private readonly LocalizationService _localization;
    private readonly Func<TweakDefinition, bool> _confirm;
    private string _filter = string.Empty;

    public TweakItemViewModel(TweakDefinition definition, TweakService service, LocalizationService localization, Func<TweakDefinition, bool> confirm)
    {
        Definition = definition;
        _service = service;
        _localization = localization;
        _confirm = confirm;
        RefreshTexts();
    }

    public TweakDefinition Definition { get; }

    public bool IsMediumRisk => Definition.Risk == TweakRisk.Medium;

    /// <summary>Gesperrt, weil die App unter einem fremden Admin-Konto läuft.</summary>
    public bool IsBlockedByAccount => _service.Engine.IsBlockedByAccount(Definition);

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _riskText = string.Empty;

    [ObservableProperty]
    private string _restartText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn), nameof(IsAvailable), nameof(IsPartial), nameof(IsVisible))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private TweakStatus _status = TweakStatus.NotApplied;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _messageIsError;

    public bool IsOn => Status == TweakStatus.Applied;

    public bool IsAvailable => Status != TweakStatus.NotAvailable;

    public bool IsPartial => Status == TweakStatus.Partial;

    /// <summary>
    /// Unpassende Tweaks (falsche Windows-Version, Dienst fehlt) werden ausgeblendet.
    /// Wegen des Kontos gesperrte bleiben sichtbar, damit der Nutzer den Grund sieht.
    /// </summary>
    public bool IsVisible => (IsAvailable || IsBlockedByAccount) && MatchesFilter();

    public void ApplyFilter(string filter)
    {
        _filter = filter.Trim();
        OnPropertyChanged(nameof(IsVisible));
    }

    public void RefreshTexts()
    {
        var lang = _localization.CurrentLanguage;
        Title = Definition.Title.Get(lang);
        Description = Definition.Description.Get(lang);
        RiskText = _localization.Get($"Risk.{Definition.Risk}");
        RestartText = Definition.Restart == RestartHint.None ? string.Empty : _localization.Get($"Restart.{Definition.Restart}");
        if (IsBlockedByAccount)
        {
            Message = _localization.Get("Tweak.BlockedAccount");
            MessageIsError = true;
        }
    }

    private bool MatchesFilter() =>
        _filter.Length == 0
        || Title.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
        || Description.Contains(_filter, StringComparison.CurrentCultureIgnoreCase);

    private bool CanToggle() => !IsBusy && IsAvailable;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        var turnOn = !IsOn;
        if (turnOn && IsMediumRisk && !_confirm(Definition))
        {
            OnPropertyChanged(nameof(IsOn));
            return;
        }

        IsBusy = true;
        Message = string.Empty;
        MessageIsError = false;
        try
        {
            var prompt = new DialogSafetyPrompt(_localization, stage => Message = stage);
            var result = turnOn ? await _service.ApplyAsync(Definition, prompt) : await _service.RevertAsync(Definition);
            Status = _service.GetStatus(Definition);
            MessageIsError = !result.Success;
            Message = result.Success
                ? _localization.Get(turnOn ? "Tweak.Applied" : "Tweak.Reverted")
                : $"{_localization.Get("Tweak.Failed")} {result.Error}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsOn));
        }
    }
}
