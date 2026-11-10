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

    /// <summary>Symbol aus der Windows-Symbolschrift (Segoe Fluent Icons, unter Windows 10 Segoe MDL2 Assets).</summary>
    public string Glyph => Glyphs.TryGetValue(TitleKey, out var glyph) ? glyph : "\uE8A5";

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["Nav.Dashboard"] = "\uE80F",
        ["Nav.Presets"] = "\uE734",
        ["Nav.Tweaks"] = "\uE9E9",
        ["Nav.Privacy"] = "\uE72E",
        ["Nav.Apps"] = "\uE71D",
        ["Nav.Processes"] = "\uE9D9",
        ["Nav.Power"] = "\uE945",
        ["Nav.Network"] = "\uE839",
        ["Nav.Health"] = "\uE95E",
        ["Nav.Debloat"] = "\uE74D",
        ["Nav.Startup"] = "\uE7E8",
        ["Nav.Care"] = "\uE90F",
        ["Nav.GameMode"] = "\uE7FC",
        ["Nav.Installer"] = "\uE896",
        ["Nav.Benchmark"] = "\uE9D2",
        ["Nav.Disk"] = "\uEDA2",
        ["Nav.Drivers"] = "\uE950",
        ["Nav.Restore"] = "\uE777",
        ["Nav.Help"] = "\uE897",
        ["Nav.Settings"] = "\uE713",
    };

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
