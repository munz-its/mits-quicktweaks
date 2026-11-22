using System.Text.Json;
using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Modules;

/// <summary>
/// Eigenes Profil als Datei (.mqtprofile, JSON): Tweaks, Energiemodus, zu entfernende Apps und Autostart.
/// Gedacht, um einen eingerichteten PC auf andere PCs zu übertragen. Beim Import zeigt die App vorher alles mit Häkchen.
/// </summary>
public sealed class ProfileFile
{
    public const string FormatName = "munz-quicktweaks-profile";
    public const int CurrentFormat = 1;
    public const string Extension = ".mqtprofile";

    /// <summary>Größer ist kein echtes Profil. Schützt vor versehentlich gewählten großen Dateien.</summary>
    public const long MaxBytes = 512 * 1024;

    /// <summary>Beim Speichern gesetzt. Fehlt es in einer Datei, ist sie kein Profil.</summary>
    public string Format { get; set; } = string.Empty;

    public int FormatVersion { get; set; } = CurrentFormat;

    public string Name { get; set; } = string.Empty;

    public string CreatedWith { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public List<string> Tweaks { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter<PowerMode>))]
    public PowerMode? PowerMode { get; set; }

    /// <summary>Paketnamen aus dem Debloat-Katalog.</summary>
    public List<string> RemoveApps { get; set; } = new();

    /// <summary>Kennungen aus AppCatalog, deren Autostart aus sein soll.</summary>
    public List<string> DisableStartup { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Serialize()
    {
        Format = FormatName;
        FormatVersion = CurrentFormat;
        return JsonSerializer.Serialize(this, Options);
    }

    /// <summary>Liest eine Profildatei. Fehler als Text, damit die Oberfläche einen klaren Hinweis zeigen kann.</summary>
    public static (ProfileFile? Profile, ProfileFileError Error) Parse(string json)
    {
        ProfileFile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ProfileFile>(json, Options);
        }
        catch (JsonException)
        {
            return (null, ProfileFileError.Invalid);
        }

        if (profile is null || profile.Format != FormatName)
        {
            return (null, ProfileFileError.Invalid);
        }

        if (profile.FormatVersion > CurrentFormat)
        {
            return (null, ProfileFileError.TooNew);
        }

        profile.Name = profile.Name.Trim();
        profile.Tweaks = Clean(profile.Tweaks);
        profile.RemoveApps = Clean(profile.RemoveApps);
        profile.DisableStartup = Clean(profile.DisableStartup);
        return (profile, ProfileFileError.None);
    }

    private static List<string> Clean(List<string>? values) =>
        (values ?? new List<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Macht daraus ein normales Profil. Unbekannte Tweaks, Apps und Autostart-Einträge (z. B. aus einer neueren Version) fallen weg
    /// und werden gezählt, damit die App darauf hinweisen kann.
    /// </summary>
    public (TweakProfile Profile, int Unknown) ToProfile(IEnumerable<string> knownTweakIds)
    {
        var known = knownTweakIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tweaks = Tweaks.Where(known.Contains).ToList();
        var apps = RemoveApps.Where(a => DebloatCatalog.All.Any(d => string.Equals(d.Package, a, StringComparison.OrdinalIgnoreCase))).ToList();
        var startup = DisableStartup.Where(s => AppCatalog.All.Any(a => string.Equals(a.Id, s, StringComparison.OrdinalIgnoreCase))).ToList();
        var unknown = (Tweaks.Count - tweaks.Count) + (RemoveApps.Count - apps.Count) + (DisableStartup.Count - startup.Count);
        var profile = new TweakProfile(ProfileKind.Custom, tweaks)
        {
            Mode = PowerMode,
            DebloatApps = apps,
            StartupApps = startup.Select(s => AppCatalog.All.First(a => string.Equals(a.Id, s, StringComparison.OrdinalIgnoreCase)).Id).ToList(),
        };
        return (profile, unknown);
    }

    public bool IsEmpty => Tweaks.Count == 0 && PowerMode is null && RemoveApps.Count == 0 && DisableStartup.Count == 0;

    /// <summary>Dateiname ohne verbotene Zeichen.</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var cleaned = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        return (cleaned.Length == 0 ? "Profil" : cleaned.Length > 60 ? cleaned[..60].Trim() : cleaned) + Extension;
    }
}

public enum ProfileFileError
{
    None,
    Invalid,
    TooNew,
}
