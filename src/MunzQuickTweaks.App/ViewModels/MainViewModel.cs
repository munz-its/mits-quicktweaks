using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MunzQuickTweaks.App.Services;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Navigation und aktuelle Seite des Hauptfensters.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly LocalizationService _localization;

    private readonly TweakService _tweaks;

    public MainViewModel(
        LocalizationService localization,
        TweakService tweakService,
        DashboardViewModel dashboard,
        TweaksViewModel tweaks,
        PrivacyViewModel privacy,
        AppsViewModel apps,
        ProcessRulesViewModel processes,
        PowerViewModel power,
        DebloatViewModel debloat,
        BenchmarkViewModel benchmark,
        DiskViewModel disk,
        DriversViewModel drivers,
        GameModeViewModel gameMode,
        StartupViewModel startup,
        CareViewModel care,
        RestoreViewModel restore,
        HelpViewModel help,
        SettingsViewModel settings,
        UpdateService updates,
        SettingsService appSettings)
    {
        _localization = localization;
        _tweaks = tweakService;
        _updates = updates;

        // Update-Leiste: nur nach Prüfung beim Start oder über die Einstellungen
        updates.AvailableChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.Invoke(RefreshUpdate);
        localization.LanguageChanged += (_, _) => RefreshUpdate();
        if (appSettings.Current.CheckForUpdates)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                await updates.CheckAsync();
            });
        }

        // Neustart-Leiste: es zählt der stärkste offene Hinweis (Explorer < Abmelden < Neustart)
        tweakService.RestartNeeded += (_, hint) =>
        {
            if (hint > PendingRestart)
            {
                PendingRestart = hint;
            }
        };

        // Pixel-Icons: 5 x 5, Zeilen mit "/" getrennt. Gruppen wie in der Seitenleiste angezeigt.
        SettingsItem = new NavItem("Nav.Settings", "10101/01110/11011/01110/10101", settings, "NavGroup.App");
        Items = new ObservableCollection<NavItem>
        {
            new("Nav.Dashboard", "11011/11011/00000/11011/11011", dashboard, "NavGroup.Start"),
            new("Nav.Tweaks", "01000/11111/00000/00010/11111", tweaks, "NavGroup.Optimize"),
            new("Nav.Privacy", "01110/10001/11111/11011/11111", privacy, "NavGroup.Optimize"),
            new("Nav.Apps", "11111/10101/11111/10101/11111", apps, "NavGroup.Optimize"),
            new("Nav.Processes", "00001/00011/00111/01111/11111", processes, "NavGroup.Optimize"),
            new("Nav.Power", "00110/01100/11111/00110/01100", power, "NavGroup.System"),
            new("Nav.Debloat", "11111/01110/01010/01010/01110", debloat, "NavGroup.System"),
            new("Nav.Startup", "00100/10101/10101/10001/01110", startup, "NavGroup.System"),
            new("Nav.Care", "00100/00100/11111/00100/00100", care, "NavGroup.System"),
            new("Nav.GameMode", "10001/11111/10101/11111/01010", gameMode, "NavGroup.Tools"),
            new("Nav.Benchmark", "00001/00010/10100/01000/00000", benchmark, "NavGroup.Tools"),
            new("Nav.Disk", "11111/10001/10111/10001/11111", disk, "NavGroup.Tools"),
            new("Nav.Drivers", "01010/11111/01110/11111/01010", drivers, "NavGroup.Tools"),
            new("Nav.Restore", "01110/10101/10111/10001/01110", restore, "NavGroup.Safety"),
            new("Nav.Help", "01110/10001/00110/00000/00100", help, "NavGroup.App"),
            SettingsItem,
        };

        _localization.LanguageChanged += (_, _) => RefreshTitles();
        dashboard.NavigateRequested = key => _ = NavigateToAsync(key);
        RefreshTitles();
    }

    public ObservableCollection<NavItem> Items { get; }

    public NavItem SettingsItem { get; }

    private readonly UpdateService _updates;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _updateText = string.Empty;

    private void RefreshUpdate()
    {
        HasUpdate = _updates.Available is not null;
        UpdateText = _updates.Available is { } info
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Update.Available"), info.Version, _updates.CurrentVersion)
            : string.Empty;
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (_updates.Available is { } info)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Kein Browser
            }
        }
    }

    [RelayCommand]
    private void DismissUpdate() => _updates.Dismiss();

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingRestart), nameof(RestartText), nameof(RestartActionText))]
    private Core.Tweaks.RestartHint _pendingRestart = Core.Tweaks.RestartHint.None;

    public bool HasPendingRestart => PendingRestart != Core.Tweaks.RestartHint.None;

    public string RestartText => HasPendingRestart ? _localization.Get($"RestartBar.{PendingRestart}") : string.Empty;

    public string RestartActionText => HasPendingRestart ? _localization.Get($"RestartBar.Action.{PendingRestart}") : string.Empty;

    [RelayCommand]
    private async Task RunRestartActionAsync()
    {
        switch (PendingRestart)
        {
            case Core.Tweaks.RestartHint.Explorer:
                PendingRestart = Core.Tweaks.RestartHint.None;
                await Windows.SessionActions.RestartExplorerAsync();
                break;

            case Core.Tweaks.RestartHint.SignOut:
                if (Confirm("RestartBar.ConfirmSignOut"))
                {
                    Windows.SessionActions.SignOut();
                }

                break;

            case Core.Tweaks.RestartHint.Reboot:
                if (Confirm("RestartBar.ConfirmReboot"))
                {
                    Windows.SessionActions.Reboot();
                }

                break;
        }
    }

    [RelayCommand]
    private void DismissRestart() => PendingRestart = Core.Tweaks.RestartHint.None;

    private bool Confirm(string key) =>
        System.Windows.MessageBox.Show(_localization.Get(key), _localization.Get("App.Name"),
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;

    /// <summary>Wird nach jeder Änderung der Auswahl ausgelöst (gleitende Markierung).</summary>
    public event EventHandler<NavItem>? SelectionChanged;

    /// <summary>Läuft vor dem Wechsel der Seite, z. B. zum Ausblenden der alten Seite.</summary>
    public Func<Task>? BeforePageChange { get; set; }

    // Parallel erlaubt: sonst wäre die Navigation gesperrt, solange eine Seite lädt
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task NavigateAsync(NavItem item)
    {
        foreach (var other in Items)
        {
            other.IsSelected = ReferenceEquals(other, item);
        }

        SelectionChanged?.Invoke(this, item);

        if (CurrentPage is not null && !ReferenceEquals(CurrentPage, item.Page) && BeforePageChange is not null)
        {
            await BeforePageChange();

            // Während des Ausblendens wurde schon ein anderer Eintrag gewählt
            if (!item.IsSelected)
            {
                return;
            }
        }

        CurrentPage = item.Page;
        await item.Page.OnNavigatedToAsync();
    }

    /// <summary>Öffnet eine Seite über ihren Titel-Schlüssel, z. B. Nav.Restore.</summary>
    public Task NavigateToAsync(string titleKey)
    {
        var item = Items.First(i => i.TitleKey == titleKey);
        return NavigateAsync(item);
    }

    private void RefreshTitles()
    {
        OnPropertyChanged(nameof(RestartText));
        OnPropertyChanged(nameof(RestartActionText));
        foreach (var item in Items)
        {
            item.Title = _localization.Get(item.TitleKey);
            item.GroupTitle = _localization.Get(item.GroupKey);
            item.Page.Title = _localization.Get(item.Page.TitleKey);
            item.Page.Heading = _localization.Get(item.Page.HeadingKey);
        }
    }
}
