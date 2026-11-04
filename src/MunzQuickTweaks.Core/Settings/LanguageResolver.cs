using System.Globalization;

namespace MunzQuickTweaks.Core.Settings;

/// <summary>Ermittelt die tatsächlich verwendete Sprache (de oder en).</summary>
public static class LanguageResolver
{
    public const string German = "de";
    public const string English = "en";

    /// <param name="setting">Gewählte Einstellung.</param>
    /// <param name="systemCulture">Windows-Anzeigesprache, z. B. de-DE.</param>
    public static string Resolve(AppLanguage setting, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);

        return setting switch
        {
            AppLanguage.German => German,
            AppLanguage.English => English,
            _ => systemCulture.TwoLetterISOLanguageName == German ? German : English,
        };
    }
}
