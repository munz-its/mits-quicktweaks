using System.Globalization;
using System.Windows;
using MunzQuickTweaks.Core.Settings;

namespace MunzQuickTweaks.App.Services;

/// <summary>
/// Tauscht das Sprach-Wörterbuch zur Laufzeit. Alle Texte in XAML nutzen DynamicResource
/// und wechseln dadurch sofort ohne Neustart.
/// </summary>
public sealed class LocalizationService
{
    private ResourceDictionary? _current;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage { get; private set; } = LanguageResolver.German;

    public void Apply(AppLanguage setting)
    {
        var language = LanguageResolver.Resolve(setting, CultureInfo.InstalledUICulture);
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Strings.{language}.xaml", UriKind.Absolute),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        merged.Add(dictionary);
        _current = dictionary;
        CurrentLanguage = language;

        var culture = new CultureInfo(language == LanguageResolver.German ? "de-DE" : "en-US");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Text für Code-Stellen, die kein DynamicResource nutzen können.</summary>
    public string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;
}
