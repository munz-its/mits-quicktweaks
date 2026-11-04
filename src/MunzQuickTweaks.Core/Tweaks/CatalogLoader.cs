using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MunzQuickTweaks.Core.Tweaks;

public sealed class CatalogException : Exception
{
    public CatalogException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Lädt und prüft den Tweak-Katalog. Ein fehlerhafter Eintrag stoppt das Laden komplett:
/// lieber kein Tweak als ein falscher.
/// </summary>
public static partial class CatalogLoader
{
    public static IReadOnlyList<TweakDefinition> Load(IEnumerable<(string Name, Stream Content)> files)
    {
        var all = new List<TweakDefinition>();
        foreach (var (name, content) in files)
        {
            List<TweakDefinition>? items;
            try
            {
                items = JsonSerializer.Deserialize<List<TweakDefinition>>(content, JournalStore.JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new CatalogException($"{name}: ungültiges JSON ({ex.Message})");
            }

            foreach (var item in items ?? new())
            {
                Validate(item, name);
                all.Add(item);
            }
        }

        var duplicate = all.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new CatalogException($"Doppelte Tweak-ID: {duplicate.Key}");
        }

        return all;
    }

    private static void Validate(TweakDefinition t, string file)
    {
        void Fail(string message) => throw new CatalogException($"{file} / {t.Id}: {message}");

        if (!IdPattern().IsMatch(t.Id))
        {
            Fail("ID muss klein geschrieben sein und Punkte enthalten, z. B. ui.fileext.show");
        }

        if (string.IsNullOrWhiteSpace(t.Title.De) || string.IsNullOrWhiteSpace(t.Title.En)
            || string.IsNullOrWhiteSpace(t.Description.De) || string.IsNullOrWhiteSpace(t.Description.En))
        {
            Fail("Titel und Beschreibung brauchen Deutsch und Englisch");
        }

        if (t.Actions.Count == 0)
        {
            Fail("Mindestens eine Aktion nötig");
        }

        foreach (var action in t.Actions)
        {
            switch (action)
            {
                case RegistryAction r:
                    if (string.IsNullOrWhiteSpace(r.Key) || string.IsNullOrWhiteSpace(r.Name))
                    {
                        Fail("Registry-Aktion braucht Key und Name");
                    }

                    if (r.Key.StartsWith('\\') || r.Key.Contains("HKEY_", StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("Key ohne Hive und ohne führenden Backslash angeben");
                    }

                    if (!IsValidData(r.Kind, r.Value) || (r.Default is not null && !IsValidData(r.Kind, r.Default)))
                    {
                        Fail($"Wert passt nicht zum Typ {r.Kind}");
                    }

                    break;

                case ServiceAction s:
                    if (!ServiceNamePattern().IsMatch(s.Name))
                    {
                        Fail("Ungültiger Dienstname");
                    }

                    break;
            }
        }
    }

    private static bool IsValidData(RegistryKind kind, string data) => kind switch
    {
        RegistryKind.DWord => uint.TryParse(data, NumberStyles.None, CultureInfo.InvariantCulture, out _),
        RegistryKind.QWord => ulong.TryParse(data, NumberStyles.None, CultureInfo.InvariantCulture, out _),
        _ => true,
    };

    [GeneratedRegex("^[a-z0-9]+(\\.[a-z0-9-]+)+$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex ServiceNamePattern();
}
