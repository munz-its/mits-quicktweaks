namespace MunzQuickTweaks.Core.Modules;

public enum DebloatGroup
{
    Ads,
    MicrosoftExtras,
    Xbox,
    Other,
}

/// <summary>Eine entfernbare App. Package = Paketname ohne Version und Herausgeber-ID.</summary>
public sealed record DebloatApp(string Package, DebloatGroup Group, string Name)
{
    /// <summary>Vorauswahl laut Entscheidung vom 04.11.2026: alles außer "Weitere".</summary>
    public bool SelectedByDefault => Group != DebloatGroup.Other;

    /// <summary>Microsoft-Store-Suche zum Neuinstallieren.</summary>
    public string StoreSearchUri => "ms-windows-store://search/?query=" + Uri.EscapeDataString(Name);
}

public sealed record InstalledPackage(string Name, string FullName);

/// <summary>App aus der Debloat-Liste mit ihren installierten Paketen.</summary>
public sealed record DebloatMatch(DebloatApp App, IReadOnlyList<InstalledPackage> Packages);

/// <summary>Paket, das noch auf dem PC liegt (vorinstalliert oder bei einem anderen Konto), aber beim Nutzer fehlt.</summary>
public sealed record RestorablePackage(string Name, string FamilyName);

/// <summary>Store-Apps des angemeldeten Nutzers.</summary>
public interface IAppxManager
{
    IReadOnlyList<InstalledPackage> ListInstalled();

    Task<Safety.OperationResult> RemoveAsync(string packageFullName);

    /// <summary>Pakete, die sich ohne Download wieder einrichten lassen.</summary>
    IReadOnlyList<RestorablePackage> ListRestorable();

    /// <summary>Richtet ein Paket aus der Windows-Kopie wieder für den Nutzer ein (Ein-Klick-Neuinstallation).</summary>
    Task<Safety.OperationResult> ReinstallAsync(string familyName);

    /// <summary>Belegter Speicher des Pakets in Byte, null wenn nicht ermittelbar.</summary>
    long? GetSize(string packageFullName);
}

/// <summary>
/// Feste Liste entfernbarer Apps. Systemwichtige Pakete (Store, App Installer, Fotos, Rechner, Edge,
/// Xbox Identity Provider für Spiele-Anmeldungen) stehen bewusst nicht drin.
/// </summary>
public static class DebloatCatalog
{
    public static readonly IReadOnlyList<DebloatApp> All = new[]
    {
        // Werbe- und Drittanbieter-Apps
        new DebloatApp("king.com.CandyCrushSaga", DebloatGroup.Ads, "Candy Crush Saga"),
        new DebloatApp("king.com.CandyCrushSodaSaga", DebloatGroup.Ads, "Candy Crush Soda Saga"),
        new DebloatApp("SpotifyAB.SpotifyMusic", DebloatGroup.Ads, "Spotify"),
        new DebloatApp("Disney.37853FC22B2CE", DebloatGroup.Ads, "Disney+"),
        new DebloatApp("BytedancePte.Ltd.TikTok", DebloatGroup.Ads, "TikTok"),
        new DebloatApp("AmazonVideo.PrimeVideo", DebloatGroup.Ads, "Prime Video"),
        new DebloatApp("Facebook.InstagramBeta", DebloatGroup.Ads, "Instagram"),
        new DebloatApp("Facebook.Facebook", DebloatGroup.Ads, "Facebook"),

        // Microsoft-Extras
        new DebloatApp("Microsoft.BingNews", DebloatGroup.MicrosoftExtras, "Microsoft News"),
        new DebloatApp("Microsoft.BingWeather", DebloatGroup.MicrosoftExtras, "MSN Wetter"),
        new DebloatApp("Microsoft.GetHelp", DebloatGroup.MicrosoftExtras, "Hilfe anfordern"),
        new DebloatApp("Microsoft.Getstarted", DebloatGroup.MicrosoftExtras, "Tipps"),
        new DebloatApp("Microsoft.WindowsFeedbackHub", DebloatGroup.MicrosoftExtras, "Feedback-Hub"),
        new DebloatApp("Microsoft.MicrosoftSolitaireCollection", DebloatGroup.MicrosoftExtras, "Microsoft Solitaire Collection"),
        new DebloatApp("Microsoft.MicrosoftOfficeHub", DebloatGroup.MicrosoftExtras, "Microsoft 365 (Office)"),
        new DebloatApp("Microsoft.People", DebloatGroup.MicrosoftExtras, "Kontakte"),
        new DebloatApp("Microsoft.WindowsMaps", DebloatGroup.MicrosoftExtras, "Karten"),
        new DebloatApp("Microsoft.Todos", DebloatGroup.MicrosoftExtras, "Microsoft To Do"),
        new DebloatApp("Clipchamp.Clipchamp", DebloatGroup.MicrosoftExtras, "Clipchamp"),
        new DebloatApp("Microsoft.PowerAutomateDesktop", DebloatGroup.MicrosoftExtras, "Power Automate"),
        new DebloatApp("Microsoft.549981C3F5F10", DebloatGroup.MicrosoftExtras, "Cortana"),
        new DebloatApp("Microsoft.Copilot", DebloatGroup.MicrosoftExtras, "Microsoft Copilot"),
        new DebloatApp("MSTeams", DebloatGroup.MicrosoftExtras, "Microsoft Teams"),
        new DebloatApp("MicrosoftTeams", DebloatGroup.MicrosoftExtras, "Microsoft Teams (Chat)"),

        // Xbox (Xbox Identity Provider und Xbox TCUI bleiben, sonst scheitern Spiele-Anmeldungen)
        new DebloatApp("Microsoft.GamingApp", DebloatGroup.Xbox, "Xbox"),
        new DebloatApp("Microsoft.XboxGamingOverlay", DebloatGroup.Xbox, "Xbox Game Bar"),
        new DebloatApp("Microsoft.XboxGameOverlay", DebloatGroup.Xbox, "Xbox Game Overlay"),
        new DebloatApp("Microsoft.XboxSpeechToTextOverlay", DebloatGroup.Xbox, "Xbox Sprache zu Text"),

        // Weitere: nicht vorausgewählt, weil viele sie nutzen
        new DebloatApp("Microsoft.YourPhone", DebloatGroup.Other, "Smartphone-Link"),
        new DebloatApp("Microsoft.ZuneVideo", DebloatGroup.Other, "Filme & TV"),
        new DebloatApp("Microsoft.OutlookForWindows", DebloatGroup.Other, "Outlook (neu)"),
        new DebloatApp("Microsoft.MicrosoftStickyNotes", DebloatGroup.Other, "Kurznotizen"),
        new DebloatApp("Microsoft.Windows.DevHome", DebloatGroup.Other, "Dev Home"),
        new DebloatApp("MicrosoftCorporationII.MicrosoftFamily", DebloatGroup.Other, "Microsoft Family"),
        new DebloatApp("Microsoft.SkypeApp", DebloatGroup.Other, "Skype"),
        new DebloatApp("Microsoft.MixedReality.Portal", DebloatGroup.Other, "Mixed Reality-Portal"),
    };

    /// <summary>Nur Apps, die beim Nutzer installiert sind, mit allen passenden Paketen (z. B. x64 und neutral).</summary>
    public static IReadOnlyList<(DebloatApp App, IReadOnlyList<InstalledPackage> Packages)> Match(IEnumerable<InstalledPackage> installed)
    {
        var byName = installed.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<InstalledPackage>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        return All.Where(a => byName.ContainsKey(a.Package))
            .Select(a => (a, byName[a.Package]))
            .ToList();
    }
}
