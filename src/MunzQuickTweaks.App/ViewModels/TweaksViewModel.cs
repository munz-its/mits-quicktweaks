using System.Collections.ObjectModel;
using System.ComponentModel;
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
        foreach (var item in Items)
        {
            item.PropertyChanged += OnItemChanged;
        }
    }

    public TweakCategory Category { get; }

    public ObservableCollection<TweakItemViewModel> Items { get; }

    /// <summary>Kategorie ausblenden, wenn kein Tweak darin sichtbar ist.</summary>
    public bool HasVisibleItems => Items.Any(i => i.IsVisible);

    [ObservableProperty]
    private string _title = string.Empty;

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TweakItemViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(HasVisibleItems));
        }
    }
}

/// <summary>Tweaks-Seite: alle Kategorien außer Datenschutz (der hat eine eigene Seite).</summary>
public partial class TweaksViewModel : PageViewModel
{
    private readonly TweakService _service;
    private readonly LocalizationService _localization;
    private readonly string _descriptionKey;

    public TweaksViewModel(TweakService service, LocalizationService localization)
        : this(service, localization, "Nav.Tweaks", "Desc.Tweaks", c => c != TweakCategory.Privacy)
    {
    }

    protected TweaksViewModel(TweakService service, LocalizationService localization, string titleKey, string descriptionKey, Func<TweakCategory, bool> categories)
        : base(titleKey)
    {
        _service = service;
        _localization = localization;
        _descriptionKey = descriptionKey;

        Groups = new ObservableCollection<TweakGroupViewModel>(
            service.Tweaks
                .Where(t => categories(t.Category))
                .GroupBy(t => t.Category)
                .OrderBy(g => g.Key)
                .Select(g => new TweakGroupViewModel(
                    g.Key,
                    g.OrderBy(t => t.Risk).Select(t => new TweakItemViewModel(t, service, localization, Confirm)))));

        CatalogError = service.CatalogError;
        RefreshGroupTitles();
        Description = localization.Get(descriptionKey);

        foreach (var item in AllItems)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(TweakItemViewModel.IsOn) or nameof(TweakItemViewModel.IsVisible))
                {
                    UpdateSummary();
                }
            };
        }

        localization.LanguageChanged += (_, _) =>
        {
            RefreshGroupTitles();
            Description = _localization.Get(_descriptionKey);
            foreach (var item in AllItems)
            {
                item.RefreshTexts();
            }

            ApplyFilter();
        };
    }

    public ObservableCollection<TweakGroupViewModel> Groups { get; }

    public IEnumerable<TweakItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    public string? CatalogError { get; }

    public bool HasCatalogError => CatalogError is not null;

    public bool IsForeignAccount => _service.IsForeignAccount;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasNoResults;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll), nameof(IsFilterLow), nameof(IsFilterMedium), nameof(IsFilterActive))]
    private TweakFilter _filter = TweakFilter.All;

    public bool IsFilterAll { get => Filter == TweakFilter.All; set { if (value) Filter = TweakFilter.All; } }

    public bool IsFilterLow { get => Filter == TweakFilter.Low; set { if (value) Filter = TweakFilter.Low; } }

    public bool IsFilterMedium { get => Filter == TweakFilter.Medium; set { if (value) Filter = TweakFilter.Medium; } }

    public bool IsFilterActive { get => Filter == TweakFilter.Active; set { if (value) Filter = TweakFilter.Active; } }

    partial void OnFilterChanged(TweakFilter value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public override async Task OnNavigatedToAsync()
    {
        // Status bei jedem Öffnen neu lesen: Windows-Updates oder andere Tools können Werte ändern
        var items = AllItems.ToList();
        var statuses = await Task.Run(() => items.Select(i => _service.GetStatus(i.Definition)).ToList());
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Status = statuses[i];
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        foreach (var item in AllItems)
        {
            item.ApplyFilter(SearchText, Filter);
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var visible = AllItems.Count(i => i.IsVisible);
        var active = AllItems.Count(i => i.IsOn);
        HasNoResults = visible == 0;
        Summary = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Tweaks.Summary"), visible, active);
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

/// <summary>Seite Datenschutz: nur die Datenschutz-Tweaks.</summary>
public sealed class PrivacyViewModel : TweaksViewModel
{
    public PrivacyViewModel(TweakService service, LocalizationService localization)
        : base(service, localization, "Nav.Privacy", "Desc.Privacy", c => c == TweakCategory.Privacy)
    {
    }
}
