using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Eine Kategorie mit ihren Tweaks.</summary>
public sealed partial class TweakGroupViewModel : ObservableObject
{
    public TweakGroupViewModel(TweakCategory category, IEnumerable<TweakItemViewModel> items)
    {
        Category = category;
        Items = new ObservableCollection<TweakItemViewModel>(items);
    }

    public TweakCategory Category { get; }

    public ObservableCollection<TweakItemViewModel> Items { get; }

    [ObservableProperty]
    private string _title = string.Empty;
}

/// <summary>Alle Tweaks, nach Kategorie gruppiert.</summary>
public sealed class TweaksViewModel : PageViewModel
{
    private readonly TweakService _service;
    private readonly LocalizationService _localization;

    public TweaksViewModel(TweakService service, LocalizationService localization)
        : base("Nav.Tweaks")
    {
        _service = service;
        _localization = localization;

        Groups = new ObservableCollection<TweakGroupViewModel>(
            service.Tweaks
                .GroupBy(t => t.Category)
                .OrderBy(g => g.Key)
                .Select(g => new TweakGroupViewModel(
                    g.Key,
                    g.OrderBy(t => t.Risk).Select(t => new TweakItemViewModel(t, service, localization, Confirm)))));

        CatalogError = service.CatalogError;
        RefreshGroupTitles();

        localization.LanguageChanged += (_, _) =>
        {
            RefreshGroupTitles();
            foreach (var item in AllItems)
            {
                item.RefreshTexts();
            }
        };
    }

    public ObservableCollection<TweakGroupViewModel> Groups { get; }

    public IEnumerable<TweakItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    public string? CatalogError { get; }

    public bool HasCatalogError => CatalogError is not null;

    public override async Task OnNavigatedToAsync()
    {
        // Status bei jedem Öffnen neu lesen: Windows-Updates oder andere Tools können Werte ändern
        var items = AllItems.ToList();
        var statuses = await Task.Run(() => items.Select(i => _service.GetStatus(i.Definition)).ToList());
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Status = statuses[i];
        }
    }

    private void RefreshGroupTitles()
    {
        foreach (var group in Groups)
        {
            group.Title = _localization.Get($"Cat.{group.Category}");
        }
    }

    private bool Confirm(TweakDefinition definition)
    {
        var text = $"{definition.Title.Get(_localization.CurrentLanguage)}\n\n{_localization.Get("Confirm.Medium")}";
        return MessageBox.Show(text, _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
