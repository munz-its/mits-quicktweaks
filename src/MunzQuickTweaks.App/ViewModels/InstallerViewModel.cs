using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

public sealed partial class InstallItemViewModel : ObservableObject
{
    public InstallItemViewModel(InstallableApp app)
    {
        App = app;
    }

    public InstallableApp App { get; }

    public string Name => App.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _state = string.Empty;

    public bool CanSelect => !IsInstalled;
}

public sealed partial class InstallGroupViewModel : ObservableObject
{
    public InstallGroupViewModel(InstallGroup group, IEnumerable<InstallItemViewModel> items)
    {
        Group = group;
        Items = new ObservableCollection<InstallItemViewModel>(items);
    }

    public InstallGroup Group { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    public ObservableCollection<InstallItemViewModel> Items { get; }
}

/// <summary>Programme gesammelt über winget installieren, direkt von der offiziellen Quelle des Herstellers.</summary>
public sealed partial class InstallerViewModel : PageViewModel
{
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<InstallerViewModel> _log;
    private string? _winget;
    private CancellationTokenSource? _cancel;

    public InstallerViewModel(HistoryStore history, LocalizationService localization, ILogger<InstallerViewModel> log)
        : base("Nav.Installer")
    {
        _history = history;
        _localization = localization;
        _log = log;
        Groups = new ObservableCollection<InstallGroupViewModel>(InstallCatalog.All
            .GroupBy(a => a.Group)
            .OrderBy(g => g.Key)
            .Select(g => new InstallGroupViewModel(g.Key, g.Select(a => new InstallItemViewModel(a)))));
        RefreshTitles();
        localization.LanguageChanged += (_, _) => RefreshTitles();
    }

    public ObservableCollection<InstallGroupViewModel> Groups { get; }

    [ObservableProperty]
    private bool _wingetMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(CancelCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    private void RefreshTitles()
    {
        foreach (var group in Groups)
        {
            group.Title = _localization.Get($"Install.Group.{group.Group}");
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _winget = await Task.Run(Windows.WindowsWinget.Find);
        WingetMissing = _winget is null;
        if (_winget is null)
        {
            return;
        }

        Status = _localization.Get("Install.Checking");
        try
        {
            var installed = await Task.Run(() => Windows.WindowsWinget.Installed(_winget));
            foreach (var item in Groups.SelectMany(g => g.Items))
            {
                item.IsInstalled = installed.Contains(item.App.WingetId);
                if (item.IsInstalled)
                {
                    item.IsSelected = false;
                }
            }

            Status = string.Empty;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or IOException)
        {
            _log.LogWarning(ex, "winget-Liste nicht lesbar");
            Status = string.Empty;
        }
    }

    private bool CanInstall() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var selected = Groups.SelectMany(g => g.Items).Where(i => i.IsSelected && !i.IsInstalled).ToList();
        if (_winget is null || selected.Count == 0)
        {
            Status = _localization.Get("Install.NothingSelected");
            return;
        }

        IsBusy = true;
        _cancel = new CancellationTokenSource();
        int ok = 0, failed = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var item = selected[i];
                Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Install.Running"), item.Name, i + 1, selected.Count);
                item.State = _localization.Get("Install.StateRunning");
                InstallResult result;
                try
                {
                    var token = _cancel.Token;
                    result = await Task.Run(() => Windows.WindowsWinget.Install(_winget, item.App, token));
                }
                catch (OperationCanceledException)
                {
                    item.State = string.Empty;
                    Status = _localization.Get("Install.Cancelled");
                    break;
                }
                catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    result = new InstallResult(false, false, ex.Message);
                }

                _history.Add(HistoryAction.AppAction, "install." + item.App.WingetId, result.Success, result.Error);
                _log.LogInformation("Installation {Id}: {Success} {Error}", item.App.WingetId, result.Success, result.Error);
                if (result.Success)
                {
                    ok++;
                    item.IsInstalled = true;
                    item.IsSelected = false;
                    item.State = _localization.Get("Install.StateDone");
                }
                else
                {
                    failed++;
                    item.State = _localization.Get("Install.StateFailed");
                    item.IsSelected = false;
                }
            }

            if (!_cancel.IsCancellationRequested)
            {
                Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Install.Done"), ok, failed);
            }
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    private static void OpenStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Windows.WindowsWinget.StoreLink) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Store nicht verfügbar
        }
    }
}
