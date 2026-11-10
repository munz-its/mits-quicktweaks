using System.Windows;

namespace MunzQuickTweaks.App.Controls;

/// <summary>
/// Gewähltes Design, fest für einen App-Start. Wechsel in den Einstellungen gilt nach einem Neustart,
/// weil die Styles beim Laden der Fenster gebunden werden.
/// </summary>
public static class UiDesign
{
    public const string Munz = "munz";
    public const string LiquidGlass = "liquidglass";

    public static bool IsLiquidGlass { get; private set; }

    public static Visibility LiquidGlassVisibility => IsLiquidGlass ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility MunzVisibility => IsLiquidGlass ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Vor dem ersten Fenster aufrufen: tauscht das Theme-Wörterbuch der App aus.</summary>
    public static void Apply(Application app, string? design)
    {
        IsLiquidGlass = design == LiquidGlass;
        if (!IsLiquidGlass)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source?.OriginalString.EndsWith("Themes/Theme.xaml", StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries[i] = new ResourceDictionary { Source = new Uri("pack://application:,,,/MunzQuickTweaks;component/Themes/ThemeApple.xaml", UriKind.Absolute) };
                return;
            }
        }
    }
}
