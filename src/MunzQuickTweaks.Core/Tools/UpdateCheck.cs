using System.Text.Json;

namespace MunzQuickTweaks.Core.Tools;

/// <summary>Inhalt von https://munz-its.de/quicktweaks/version.json.</summary>
public sealed class UpdateInfo
{
    public string Version { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? NotesDe { get; set; }

    public string? NotesEn { get; set; }
}

public static class UpdateCheck
{
    public const string VersionUrl = "https://munz-its.de/quicktweaks/version.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Liest die Datei. Nur Links zu GitHub oder munz-its.de werden angenommen.</summary>
    public static UpdateInfo Parse(string json)
    {
        var info = JsonSerializer.Deserialize<UpdateInfo>(json, Options) ?? throw new FormatException("Leere Update-Datei.");
        if (!System.Version.TryParse(info.Version, out _))
        {
            throw new FormatException("Ungültige Versionsnummer in der Update-Datei.");
        }

        if (!Uri.TryCreate(info.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("munz-its.de", StringComparison.OrdinalIgnoreCase)))
        {
            throw new FormatException("Ungültiger Download-Link in der Update-Datei.");
        }

        return info;
    }

    /// <summary>True, wenn die angebotene Version neuer ist als die laufende.</summary>
    public static bool IsNewer(UpdateInfo info, string current) =>
        System.Version.TryParse(Normalize(info.Version), out var offered)
        && System.Version.TryParse(Normalize(current), out var running)
        && offered > running;

    private static string Normalize(string version)
    {
        var plus = version.IndexOfAny(new[] { '+', '-' });
        var core = plus >= 0 ? version[..plus] : version;
        return core.Count(c => c == '.') == 1 ? core + ".0" : core;
    }
}
