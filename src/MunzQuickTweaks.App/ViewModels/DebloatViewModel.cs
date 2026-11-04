using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.App.ViewModels;

public sealed partial class DebloatItemViewModel : ObservableObject
{
    public DebloatItemViewModel(DebloatApp app, IReadOnlyList<InstalledPackage> packages)
    {
        App = app;
        Packages = packages;
        _isSelected = app.SelectedByDefault;
    }

    public DebloatApp App { get; }

    public IReadOnlyList<InstalledPackage> Packages { get; }

    public string Name => App.Name;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class DebloatGroupViewModel : ObservableObject
{
    public DebloatGroupViewModel(DebloatGroup group, IEnumerable<DebloatItemViewModel> items)
    {
        Group = group;
        Items = new ObservableCollection<DebloatItemViewModel>(items);
    }

    public DebloatGroup Group { get; }

    public ObservableCollection<DebloatItemViewModel> Items { get; }

    public bool IsXbox => Group == DebloatGroup.Xbox;

    [ObservableProperty]
    private string _title = string.Empty;
}

public sealed record RemovedAppRow(string Name, string Time, string StoreUri);

/// <summary>Vorinstallierte Apps entfernen, nur für den angemeldeten Nutzer, über den Store neu installierbar.</summary>
public sealed partial class DebloatViewModel : PageViewModel
{
    private readonly IAppxManager _appx;
    private readonly RestorePointGate _gate;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly TweakService _tweaks;
    private readonly ILogger<DebloatViewModel> _log;

    public DebloatViewModel(IAppxManager appx, RestorePointGate gate, HistoryStore history, LocalizationService localization, TweakService tweaks, ILogger<DebloatViewModel> log)
        : base("Nav.Debloat")
    {
        _appx = appx;
        _gate = gate;
        _history = history;
        _localization = localization;
        _tweaks = tweaks;
        _log = log;
        localization.LanguageChanged += (_, _) => RefreshTitles();
    }

    public ObservableCollection<DebloatGroupViewModel> Groups { get; } = new();

    public ObservableCollection<RemovedAppRow> Removed { get; } = new();

    /// <summary>Bei fremdem Admin-Konto würden Apps im falschen Konto entfernt: gesperrt.</summary>
    public bool IsForeignAccount => _tweaks.IsForeignAccount;

    public bool HasApps => Groups.Count > 0;

    public bool HasRemoved => Removed.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _status = string.Empty;

    public override Task OnNavigatedToAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var matched = await Task.Run(() => DebloatCatalog.Match(_appx.ListInstalled()));
            Groups.Clear();
            foreach (var group in matched.GroupBy(m => m.App.Group).OrderBy(g => g.Key))
            {
                Groups.Add(new DebloatGroupViewModel(group.Key, group.Select(m => new DebloatItemViewModel(m.App, m.Packages))));
            }

            RefreshTitles();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Installierte Apps nicht lesbar");
            Status = _localization.Get("Debloat.ListFailed");
        }
        finally
        {
            IsLoading = false;
        }

        Removed.Clear();
        foreach (var entry in _history.Latest(200).Where(h => h.Action == HistoryAction.AppRemoved && h.Success))
        {
            var app = DebloatCatalog.All.FirstOrDefault(a => a.Package == entry.TweakId);
            if (app is not null && Removed.All(r => r.Name != app.Name))
            {
                Removed.Add(new RemovedAppRow(app.Name, entry.Time.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture), app.StoreSearchUri));
            }
        }

        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(HasRemoved));
    }

    private void RefreshTitles()
    {
        foreach (var group in Groups)
        {
            group.Title = _localization.Get($"Debloat.Group.{group.Group}");
        }
    }

    private bool CanRemove() => !IsBusy && !IsForeignAccount;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveSelectedAsync()
    {
        var selected = Groups.SelectMany(g => g.Items).Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = _localization.Get("Debloat.NothingSelected");
            return;
        }

        var list = string.Join("\n", selected.Select(s => "• " + s.Name));
        var text = $"{_localization.Get("Debloat.Confirm")}\n\n{list}";
        if (MessageBox.Show(text, _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var prompt = new DialogSafetyPrompt(_localization, stage => Status = stage);
            if (await Task.Run(() => _gate.Ensure(prompt)) == GateResult.Cancelled)
            {
                Status = string.Empty;
                return;
            }

            int ok = 0, failed = 0;
            foreach (var item in selected)
            {
                Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Debloat.Removing"), item.Name);
                var allRemoved = true;
                string? error = null;
                foreach (var package in item.Packages)
                {
                    var result = await _appx.RemoveAsync(package.FullName);
                    if (!result.Success)
                    {
                        allRemoved = false;
                        error = result.Error;
                    }
                }

                _history.Add(HistoryAction.AppRemoved, item.App.Package, allRemoved, error);
                _log.LogInformation("App {Package} entfernt: {Success} {Error}", item.App.Package, allRemoved, error);
                if (allRemoved)
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Debloat.Done"), ok, failed);
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void OpenStore(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            CrashLog.Write("Microsoft Store konnte nicht geöffnet werden", ex);
        }
    }
}
