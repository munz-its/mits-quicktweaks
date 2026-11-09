using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.ViewModels;

public sealed partial class PowerPlanItemViewModel : ObservableObject
{
    public PowerPlanItemViewModel(PowerPlanInfo info)
    {
        Info = info;
        _isActive = info.IsActive;
    }

    public PowerPlanInfo Info { get; }

    public string Name => Info.Name;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>Energiemodus (wie in den Windows-Einstellungen) und Energieplan, wichtig vor allem auf Laptops.</summary>
public sealed partial class PowerViewModel : PageViewModel
{
    private readonly IPowerModes _power;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<PowerViewModel> _log;
    private bool _loading;

    public PowerViewModel(IPowerModes power, HistoryStore history, LocalizationService localization, ILogger<PowerViewModel> log)
        : base("Nav.Power")
    {
        _power = power;
        _history = history;
        _localization = localization;
        _log = log;
    }

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; } = new();

    /// <summary>Bei Modern Standby gibt es nur einen Plan: dann ist die Auswahl überflüssig.</summary>
    public bool HasPlanChoice => Plans.Count > 1;

    [ObservableProperty]
    private bool _modeAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEfficiency), nameof(IsBalanced), nameof(IsPerformance))]
    private PowerMode? _mode;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _statusIsError;

    public bool IsEfficiency { get => Mode == PowerMode.Efficiency; set { if (value) SetMode(PowerMode.Efficiency); } }

    public bool IsBalanced { get => Mode == PowerMode.Balanced; set { if (value) SetMode(PowerMode.Balanced); } }

    public bool IsPerformance { get => Mode == PowerMode.Performance; set { if (value) SetMode(PowerMode.Performance); } }

    public override Task OnNavigatedToAsync()
    {
        Status = string.Empty;
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var (available, mode, plans) = await Task.Run(() =>
            {
                try
                {
                    return (_power.ModeAvailable, _power.GetMode(), _power.ListPlans());
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    _log.LogWarning(ex, "Energieeinstellungen nicht lesbar");
                    return (false, (PowerMode?)null, (IReadOnlyList<PowerPlanInfo>)Array.Empty<PowerPlanInfo>());
                }
            });

            ModeAvailable = available;
            Mode = mode;
            Plans.Clear();
            foreach (var plan in plans)
            {
                Plans.Add(new PowerPlanItemViewModel(plan));
            }

            OnPropertyChanged(nameof(HasPlanChoice));
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetMode(PowerMode mode)
    {
        if (_loading || mode == Mode)
        {
            return;
        }

        try
        {
            _power.SetMode(mode);
            Mode = _power.GetMode() ?? mode;
            _history.Add(HistoryAction.PowerModeChanged, $"mode.{mode}", true, null);
            _log.LogInformation("Energiemodus {Mode}", mode);
            StatusIsError = false;
            Status = _localization.Get("Power.ModeSet");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _history.Add(HistoryAction.PowerModeChanged, $"mode.{mode}", false, ex.Message);
            StatusIsError = true;
            Status = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
            OnPropertyChanged(nameof(IsEfficiency));
            OnPropertyChanged(nameof(IsBalanced));
            OnPropertyChanged(nameof(IsPerformance));
        }
    }

    [RelayCommand]
    private async Task ActivatePlanAsync(PowerPlanItemViewModel plan)
    {
        try
        {
            _power.ActivatePlan(plan.Info.Id);
            _history.Add(HistoryAction.PowerModeChanged, $"plan.{plan.Info.Id}", true, plan.Name);
            _log.LogInformation("Energieplan {Plan}", plan.Name);
            StatusIsError = false;
            Status = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Power.PlanSet"), plan.Name);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            StatusIsError = true;
            Status = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }

        await LoadAsync();
    }

    [RelayCommand]
    private Task UseBalancedAsync() =>
        ActivatePlanAsync(Plans.FirstOrDefault(p => p.Info.Id == TweakEngine.BalancedScheme)
                          ?? new PowerPlanItemViewModel(new PowerPlanInfo(TweakEngine.BalancedScheme, "Ausbalanciert", false)));
}
