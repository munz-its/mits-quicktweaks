using CommunityToolkit.Mvvm.ComponentModel;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Ein Eintrag in der Seitennavigation.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string titleKey, string iconPattern, PageViewModel page, string groupKey = "")
    {
        TitleKey = titleKey;
        IconPattern = iconPattern;
        Page = page;
        GroupKey = groupKey;
    }

    /// <summary>Gruppe in der Seitenleiste, z. B. NavGroup.Optimize. Leer = ohne Überschrift.</summary>
    public string GroupKey { get; }

    [ObservableProperty]
    private string _groupTitle = string.Empty;

    /// <summary>Schlüssel im Sprach-Wörterbuch, z. B. Nav.Tweaks.</summary>
    public string TitleKey { get; }

    /// <summary>5 x 5 Pixel-Icon, Zeilen getrennt durch "/", 1 = Pixel gesetzt.</summary>
    public string IconPattern { get; }

    public PageViewModel Page { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
