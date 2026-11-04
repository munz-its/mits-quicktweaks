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

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _riskText = string.Empty;

    [ObservableProperty]
    private string _restartText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn), nameof(IsAvailable), nameof(IsPartial))]
    private TweakStatus _status;

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

    public void RefreshStatus() => Status = _service.GetStatus(Definition);

    public void RefreshTexts()
    {
        var lang = _localization.CurrentLanguage;
        Title = Definition.Title.Get(lang);
        Description = Definition.Description.Get(lang);
        Category = _localization.Get($"Cat.{Definition.Category}");
        RiskText = _localization.Get($"Risk.{Definition.Risk}");
        RestartText = Definition.Restart == RestartHint.None ? string.Empty : _localization.Get($"Restart.{Definition.Restart}");
    }

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
            RefreshStatus();
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
