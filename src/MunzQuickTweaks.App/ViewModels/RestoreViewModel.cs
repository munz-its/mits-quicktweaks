using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.App.ViewModels;

public sealed record RestorePointRow(string Date, string Description);

public sealed record ActiveTweakRow(string Title, string Since);

public sealed record HistoryRow(string Time, string Text, bool Success);

/// <summary>Systemschutz, Wiederherstellungspunkte, aktive Tweaks und Änderungsprotokoll.</summary>
public sealed partial class RestoreViewModel : PageViewModel
{
    private readonly IRestorePointService _restorePoints;
    private readonly TweakService _tweaks;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;

    public RestoreViewModel(IRestorePointService restorePoints, TweakService tweaks, HistoryStore history, LocalizationService localization)
        : base("Nav.Restore")
    {
        _restorePoints = restorePoints;
        _tweaks = tweaks;
        _history = history;
        _localization = localization;
    }

    public ObservableCollection<RestorePointRow> RestorePoints { get; } = new();

    public ObservableCollection<ActiveTweakRow> ActiveTweaks { get; } = new();

    public ObservableCollection<HistoryRow> History { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnableProtection), nameof(IsProtectionOn))]
    private ProtectionState _protection = ProtectionState.Unknown;

    [ObservableProperty]
    private string _protectionText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRestorePointCommand), nameof(EnableProtectionCommand), nameof(RevertAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    public bool CanEnableProtection => Protection == ProtectionState.Disabled;

    public bool IsProtectionOn => Protection == ProtectionState.Enabled;

    public bool HasActiveTweaks => ActiveTweaks.Count > 0;

    public override Task OnNavigatedToAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        var data = await Task.Run(() => (
            Protection: _restorePoints.GetProtectionState(),
            Points: _restorePoints.List(),
            Journal: _tweaks.Engine.Journal.All,
            History: _history.Latest(50)));

        Protection = data.Protection;
        ProtectionText = _localization.Get($"Protection.{data.Protection}");

        RestorePoints.Clear();
        foreach (var point in data.Points.Take(8))
        {
            RestorePoints.Add(new RestorePointRow(point.Created.ToString("g", CultureInfo.CurrentUICulture), point.Description));
        }

        var lang = _localization.CurrentLanguage;
        ActiveTweaks.Clear();
        foreach (var entry in data.Journal.OrderByDescending(e => e.AppliedAt))
        {
            var title = _tweaks.Find(entry.TweakId)?.Title.Get(lang) ?? entry.TweakId;
            ActiveTweaks.Add(new ActiveTweakRow(title, entry.AppliedAt.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture)));
        }

        OnPropertyChanged(nameof(HasActiveTweaks));

        History.Clear();
        foreach (var item in data.History)
        {
            var subject = item.TweakId is null ? string.Empty : ": " + (_tweaks.Find(item.TweakId)?.Title.Get(lang) ?? item.TweakId);
            var text = _localization.Get($"History.{item.Action}") + subject;
            if (!item.Success && !string.IsNullOrEmpty(item.Message))
            {
                text += $" ({item.Message})";
            }

            History.Add(new HistoryRow(item.Time.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture), text, item.Success));
        }
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CreateRestorePointAsync()
    {
        IsBusy = true;
        Status = _localization.Get("Safety.Stage.create");
        try
        {
            var result = await Task.Run(() => _restorePoints.Create(RestorePointGate.Description));
            _history.Add(HistoryAction.RestorePointCreated, null, result.Success, result.Error);
            Status = result.Success ? _localization.Get("Restore.Created") : $"{_localization.Get("Safety.CreateFailed")} {result.Error}";
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task EnableProtectionAsync()
    {
        IsBusy = true;
        Status = _localization.Get("Safety.Stage.enable");
        try
        {
            var result = await Task.Run(_restorePoints.EnableProtection);
            _history.Add(HistoryAction.ProtectionEnabled, null, result.Success, result.Error);
            Status = result.Success ? string.Empty : result.Error ?? string.Empty;
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RevertAllAsync()
    {
        if (MessageBox.Show(_localization.Get("Restore.ConfirmRevertAll"), _localization.Get("App.Name"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var errors = await _tweaks.RevertAllAsync();
            Status = errors == 0 ? _localization.Get("Restore.AllReverted") : string.Format(CultureInfo.CurrentCulture, _localization.Get("Restore.RevertErrors"), errors);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void OpenSystemRestore() => Start(Path.Combine(Environment.SystemDirectory, "rstrui.exe"));

    [RelayCommand]
    private static void OpenBackupFolder()
    {
        Directory.CreateDirectory(AppPaths.MachineData);
        Start("explorer.exe", AppPaths.MachineData);
    }

    private static void Start(string file, string? argument = null)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = true };
            if (argument is not null)
            {
                info.ArgumentList.Add(argument);
            }

            Process.Start(info)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            CrashLog.Write($"{file} konnte nicht geöffnet werden", ex);
        }
    }
}
