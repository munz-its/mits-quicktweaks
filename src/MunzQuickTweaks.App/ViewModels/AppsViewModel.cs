using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Ein Programm mit Autostart-Schalter und seinen Tweaks.</summary>
public sealed partial class AppSectionViewModel : ObservableObject
{
    private readonly AppsViewModel _owner;

    public AppSectionViewModel(AppProfile profile, IEnumerable<TweakItemViewModel> tweaks, AppsViewModel owner)
    {
        Profile = profile;
        _owner = owner;
        Tweaks = new ObservableCollection<TweakItemViewModel>(tweaks);
    }

    public AppProfile Profile { get; }

    public string Name => Profile.Name;

    public bool IsOneDrive => Profile.Id == "onedrive";

    public ObservableCollection<TweakItemViewModel> Tweaks { get; }

    public bool HasTweaks => Tweaks.Count > 0;

    public IReadOnlyList<StartupEntry> StartupEntries { get; set; } = Array.Empty<StartupEntry>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUninstall), nameof(ShowInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _hasAutostart;

    [ObservableProperty]
    private bool _autostartOn;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _messageIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand), nameof(InstallCommand))]
    private bool _isBusy;

    public bool ShowUninstall => IsOneDrive && IsInstalled;

    public bool ShowInstall => IsOneDrive && !IsInstalled;

    [RelayCommand]
    private void ToggleAutostart() => _owner.SetAutostart(this, AutostartOn);

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UninstallAsync() => _owner.UninstallOneDriveAsync(this);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Install() => _owner.InstallOneDrive(this);
}

/// <summary>Seite Apps: Edge, Chrome, Discord, OneDrive, Steam und Epic Games gezielt einstellen.</summary>
public sealed partial class AppsViewModel : PageViewModel
{
    private readonly TweakService _service;
    private readonly IStartupManager _startup;
    private readonly IAppEnvironment _environment;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<AppsViewModel> _log;

    public AppsViewModel(TweakService service, IStartupManager startup, IAppEnvironment environment, HistoryStore history,
        LocalizationService localization, ChangeLedger ledger, ILogger<AppsViewModel> log)
        : base("Nav.Apps")
    {
        _service = service;
        _startup = startup;
        _ledger = ledger;
        _environment = environment;
        _history = history;
        _localization = localization;
        _log = log;

        var tweaks = service.Tweaks.Where(t => t.Category == TweakCategory.Apps).ToList();
        foreach (var profile in AppCatalog.All)
        {
            var items = tweaks.Where(t => t.App == profile.Id)
                .Select(t => new TweakItemViewModel(t, service, localization, Confirm));
            _all.Add(new AppSectionViewModel(profile, items, this));
        }

        localization.LanguageChanged += (_, _) =>
        {
            foreach (var item in _all.SelectMany(a => a.Tweaks))
            {
                item.RefreshTexts();
            }

            UpdateMissingText();
        };
    }

    private readonly List<AppSectionViewModel> _all = new();
    private readonly ChangeLedger _ledger;

    /// <summary>Installierte Programme. OneDrive bleibt auch deinstalliert sichtbar, für die Neuinstallation.</summary>
    public ObservableCollection<AppSectionViewModel> Sections { get; } = new();

    public bool IsForeignAccount => _service.IsForeignAccount;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _missingText = string.Empty;

    public bool HasMissing => MissingText.Length > 0;

    partial void OnMissingTextChanged(string value) => OnPropertyChanged(nameof(HasMissing));

    public override async Task OnNavigatedToAsync()
    {
        IsLoading = true;
        try
        {
            var data = await Task.Run(() =>
            {
                IReadOnlyList<StartupEntry> entries;
                try
                {
                    entries = _startup.List();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
                {
                    entries = Array.Empty<StartupEntry>();
                }

                return _all.Select(section =>
                {
                    var startup = AppCatalog.MatchStartup(section.Profile, entries);
                    var installed = section.Profile.Paths.Any(_environment.FileExists) || startup.Count > 0;
                    var statuses = section.Tweaks.Select(t => _service.GetStatus(t.Definition)).ToList();
                    return (section, installed, startup, statuses);
                }).ToList();
            });

            Sections.Clear();
            foreach (var (section, installed, startup, statuses) in data)
            {
                section.IsInstalled = installed;
                section.StartupEntries = startup;
                section.HasAutostart = startup.Count > 0;
                section.AutostartOn = startup.Any(e => e.Enabled);
                for (var i = 0; i < section.Tweaks.Count; i++)
                {
                    section.Tweaks[i].Status = statuses[i];
                    section.Tweaks[i].ApplyFilter(string.Empty, TweakFilter.All);
                }

                if (installed || section.IsOneDrive)
                {
                    Sections.Add(section);
                }
            }

            UpdateMissingText();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateMissingText()
    {
        var missing = _all.Where(a => !a.IsInstalled && !a.IsOneDrive).Select(a => a.Name).ToList();
        MissingText = missing.Count == 0 ? string.Empty : $"{_localization.Get("Apps.NotInstalled")} {string.Join(", ", missing)}";
    }

    internal void SetAutostart(AppSectionViewModel section, bool enable)
    {
        var failed = new List<string>();
        foreach (var entry in section.StartupEntries)
        {
            var result = _startup.SetEnabled(entry, enable);
            if (result.Success)
            {
                _ledger.RecordStartup(entry.Id, entry.Enabled);
            }

            _history.Add(enable ? HistoryAction.StartupEnabled : HistoryAction.StartupDisabled, null, result.Success,
                result.Success ? entry.Name : $"{entry.Name}: {result.Error}");
            if (!result.Success)
            {
                failed.Add(result.Error ?? entry.Name);
            }
        }

        section.MessageIsError = failed.Count > 0;
        section.Message = failed.Count == 0
            ? _localization.Get(enable ? "Apps.AutostartOn" : "Apps.AutostartOff")
            : $"{_localization.Get("Tweak.Failed")} {string.Join(", ", failed)}";
        _log.LogInformation("Autostart {App}: {State}, Fehler {Failed}", section.Name, enable ? "an" : "aus", failed.Count);
        _ = OnNavigatedToAsync();
    }

    internal async Task UninstallOneDriveAsync(AppSectionViewModel section)
    {
        if (MessageBox.Show(_localization.Get("Apps.OneDrive.ConfirmUninstall"), _localization.Get("App.Name"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        section.IsBusy = true;
        section.MessageIsError = false;
        section.Message = _localization.Get("Apps.OneDrive.Uninstalling");
        try
        {
            var result = await Windows.OneDriveActions.UninstallAsync();
            _history.Add(HistoryAction.AppAction, "onedrive.uninstall", result.Success, result.Error);
            if (result.Success)
            {
                _ledger.RecordOneDrive(true);
            }

            _log.LogInformation("OneDrive deinstalliert: {Success} {Error}", result.Success, result.Error);
            section.MessageIsError = !result.Success;
            section.Message = result.Success ? _localization.Get("Apps.OneDrive.Uninstalled") : $"{_localization.Get("Tweak.Failed")} {result.Error}";
        }
        finally
        {
            section.IsBusy = false;
        }

        var message = section.Message;
        var isError = section.MessageIsError;
        await OnNavigatedToAsync();
        section.Message = message;
        section.MessageIsError = isError;
    }

    internal void InstallOneDrive(AppSectionViewModel section)
    {
        var started = Windows.OneDriveActions.StartInstall();
        _ledger.RecordOneDrive(false);
        _history.Add(HistoryAction.AppAction, "onedrive.install", true, started ? "Setup gestartet" : "Download-Seite geöffnet");
        section.MessageIsError = false;
        section.Message = _localization.Get(started ? "Apps.OneDrive.InstallStarted" : "Apps.OneDrive.DownloadOpened");
    }

    private bool Confirm(TweakDefinition definition)
    {
        var text = $"{definition.Title.Get(_localization.CurrentLanguage)}\n\n{_localization.Get("Confirm.Medium")}";
        return MessageBox.Show(text, _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
