using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MunzQuickTweaks.App.Services;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Navigation und aktuelle Seite des Hauptfensters.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly LocalizationService _localization;

    public MainViewModel(
        LocalizationService localization,
        DashboardViewModel dashboard,
        TweaksViewModel tweaks,
        PrivacyViewModel privacy,
        DebloatViewModel debloat,
        StartupViewModel startup,
        CareViewModel care,
        RestoreViewModel restore,
        SettingsViewModel settings)
    {
        _localization = localization;

        // Pixel-Icons: 5 x 5, Zeilen mit "/" getrennt
        Items = new ObservableCollection<NavItem>
        {
            new("Nav.Dashboard", "11011/11011/00000/11011/11011", dashboard),
            new("Nav.Tweaks", "01000/11111/00000/00010/11111", tweaks),
            new("Nav.Privacy", "01110/10001/11111/11011/11111", privacy),
            new("Nav.Debloat", "11111/01110/01010/01010/01110", debloat),
            new("Nav.Startup", "00100/10101/10101/10001/01110", startup),
            new("Nav.Care", "00100/00100/11111/00100/00100", care),
            new("Nav.Restore", "01110/10101/10111/10001/01110", restore),
        };
        SettingsItem = new NavItem("Nav.Settings", "10101/01110/11011/01110/10101", settings);

        _localization.LanguageChanged += (_, _) => RefreshTitles();
        dashboard.NavigateRequested = key => _ = NavigateToAsync(key);
        RefreshTitles();
    }

    public ObservableCollection<NavItem> Items { get; }

    public NavItem SettingsItem { get; }

    [ObservableProperty]
    private PageViewModel? _currentPage;

    /// <summary>Wird nach jeder Änderung der Auswahl ausgelöst (gleitende Markierung).</summary>
    public event EventHandler<NavItem>? SelectionChanged;

    /// <summary>Läuft vor dem Wechsel der Seite, z. B. zum Ausblenden der alten Seite.</summary>
    public Func<Task>? BeforePageChange { get; set; }

    // Parallel erlaubt: sonst wäre die Navigation gesperrt, solange eine Seite lädt
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task NavigateAsync(NavItem item)
    {
        foreach (var other in Items.Append(SettingsItem))
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
        var item = Items.Append(SettingsItem).First(i => i.TitleKey == titleKey);
        return NavigateAsync(item);
    }

    private void RefreshTitles()
    {
        foreach (var item in Items.Append(SettingsItem))
        {
            item.Title = _localization.Get(item.TitleKey);
            item.Page.Title = _localization.Get(item.Page.TitleKey);
            item.Page.Heading = _localization.Get(item.Page.HeadingKey);
        }
    }
}
