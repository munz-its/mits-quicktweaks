using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.ViewModels;

public sealed partial class CleanupItemViewModel : ObservableObject
{
    public CleanupItemViewModel(CleanupTarget target)
    {
        Target = target;
    }

    public CleanupTarget Target { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    [ObservableProperty]
    private string _size = "…";

    [ObservableProperty]
    private bool _isSelected = true;
}

/// <summary>Aufräumen und Reparatur.</summary>
public sealed partial class CareViewModel : PageViewModel
{
    private readonly WindowsCleanup _cleanup;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<CareViewModel> _log;

    public CareViewModel(WindowsCleanup cleanup, HistoryStore history, LocalizationService localization, ILogger<CareViewModel> log)
        : base("Nav.Care")
    {
        _cleanup = cleanup;
        _history = history;
        _localization = localization;
        _log = log;
        Items = new ObservableCollection<CleanupItemViewModel>(Enum.GetValues<CleanupTarget>().Select(t => new CleanupItemViewModel(t)));
        RefreshTexts();
        localization.LanguageChanged += (_, _) => RefreshTexts();
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand), nameof(FlushDnsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _total = string.Empty;

    public override Task OnNavigatedToAsync() => MeasureAsync();

    private void RefreshTexts()
    {
        foreach (var item in Items)
        {
            item.Title = _localization.Get($"Care.{item.Target}");
            item.Hint = _localization.Get($"Care.{item.Target}.Hint");
        }
    }

    private async Task MeasureAsync()
    {
        long total = 0;
        foreach (var item in Items)
        {
            item.Size = "…";
        }

        foreach (var item in Items)
        {
            var bytes = await Task.Run(() =>
            {
                try
                {
                    return _cleanup.Measure(item.Target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return 0L;
                }
            });
            item.Size = FolderCleaner.FormatSize(bytes);
            total += bytes;
        }

        Total = FolderCleaner.FormatSize(total);
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        IsBusy = true;
        Status = _localization.Get("Care.Cleaning");
        try
        {
            var result = CleanupResult.Empty;
            foreach (var item in selected)
            {
                var part = await Task.Run(() => _cleanup.Clean(item.Target));
                _log.LogInformation("Aufräumen {Target}: {Files} Dateien, {Bytes} Bytes, {Skipped} übersprungen", item.Target, part.Files, part.Bytes, part.Skipped);
                result = result.Add(part);
            }

            var freed = FolderCleaner.FormatSize(result.Bytes);
            _history.Add(HistoryAction.CleanupDone, null, true, freed);
            Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Care.Done"), freed, result.Skipped);
            await MeasureAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task FlushDnsAsync()
    {
        var ok = await Task.Run(_cleanup.FlushDns);
        Status = _localization.Get(ok ? "Care.DnsDone" : "Care.DnsFailed");
    }

    [RelayCommand]
    private void Repair(string tool)
    {
        var repair = Enum.Parse<RepairTool>(tool);
        if (MessageBox.Show(_localization.Get($"Care.Confirm.{repair}"), _localization.Get("App.Name"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _cleanup.StartRepair(repair);
            _log.LogInformation("Reparatur gestartet: {Tool}", repair);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.LogError(ex, "Reparatur {Tool} nicht startbar", repair);
            Status = ex.Message;
        }
    }
}
