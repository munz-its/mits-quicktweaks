using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Tweaks;

public enum TweakCategory
{
    Performance,
    Gaming,
    Privacy,
    Interface,
    Network,
    Services,
    Apps,
}

/// <summary>Niedrig: wird direkt angewendet. Mittel: Bestätigungsdialog. Hoch gibt es bewusst nicht.</summary>
public enum TweakRisk
{
    Low,
    Medium,
}

/// <summary>Was nötig ist, bis der Tweak wirkt.</summary>
public enum RestartHint
{
    None,
    Explorer,
    SignOut,
    Reboot,
}

/// <summary>Text in Deutsch und Englisch, direkt im Katalog gepflegt.</summary>
public sealed class LocalizedText
{
    public string De { get; set; } = string.Empty;

    public string En { get; set; } = string.Empty;

    public string Get(string language) => language == "de" ? De : En;
}

/// <summary>Ein Tweak aus dem Katalog (catalog/*.json).</summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = string.Empty;

    public TweakCategory Category { get; set; }

    public TweakRisk Risk { get; set; }

    public RestartHint Restart { get; set; }

    /// <summary>Niedrigster unterstützter Build, z. B. 19045 für Windows 10 22H2.</summary>
    public int MinBuild { get; set; } = 19045;

    /// <summary>Höchster unterstützter Build, 0 = keine Grenze.</summary>
    public int MaxBuild { get; set; }

    public LocalizedText Title { get; set; } = new();

    public LocalizedText Description { get; set; } = new();

    public List<TweakAction> Actions { get; set; } = new();

    /// <summary>Nur für Kategorie Apps: Programm-Kennung, z. B. "edge", "discord".</summary>
    public string? App { get; set; }

    /// <summary>
    /// Dateien mit Umgebungsvariablen. Ist die Liste gefüllt, ist der Tweak nur verfügbar,
    /// wenn mindestens eine davon existiert (Programm installiert).
    /// </summary>
    public List<string> Requires { get; set; } = new();

    [JsonIgnore]
    public bool IsWindows11Only => MinBuild >= Machine.OsNaming.FirstWindows11Build;

    public bool SupportsBuild(int build) => build >= MinBuild && (MaxBuild == 0 || build <= MaxBuild);
}
