using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Core.Modules;

public enum ProfileKind
{
    Recommended,
    Gaming,
    Privacy,
    ResetAll,
}

/// <summary>Ein Klick für mehrere Tweaks. Inhalte am 04.11.2026 mit Mert abgestimmt.</summary>
public sealed record TweakProfile(ProfileKind Kind, IReadOnlyList<string> TweakIds);

public static class ProfileCatalog
{
    public static readonly TweakProfile Recommended = new(ProfileKind.Recommended, new[]
    {
        "ui.fileext.show", "privacy.adid.off", "privacy.tips.off", "privacy.silentapps.off", "privacy.lockscreen.ads",
        "privacy.tailored.off", "privacy.feedback.off", "privacy.websearch.off", "perf.startupdelay.off",
        "perf.menudelay.short", "ui.endtask.on", "net.deliveryopt.p2p.off", "gaming.stickykeys.off",
    });

    public static readonly TweakProfile Gaming = new(ProfileKind.Gaming, new[]
    {
        "gaming.gamedvr.off", "gaming.mouseaccel.off", "gaming.stickykeys.off", "gaming.windowed.optimize",
        "gaming.mmcss.games", "gaming.responsiveness", "perf.startupdelay.off",
        "gaming.hags.on", "gaming.powerthrottling.off", "net.throttling.off", "perf.powerplan.ultimate",
    });

    /// <summary>Alle Datenschutz-Tweaks mit Risiko niedrig, dazu Telemetrie-Dienst, Copilot und Recall. Ortung bewusst nicht.</summary>
    public static TweakProfile Privacy(IEnumerable<TweakDefinition> catalog) => new(ProfileKind.Privacy,
        catalog.Where(t => t.Category == TweakCategory.Privacy && t.Risk == TweakRisk.Low)
            .Select(t => t.Id)
            .Concat(new[] { "services.diagtrack.off", "privacy.copilot.off", "privacy.recall.off" })
            .Distinct()
            .ToList());

    public static TweakProfile Get(ProfileKind kind, IEnumerable<TweakDefinition> catalog) => kind switch
    {
        ProfileKind.Recommended => Recommended,
        ProfileKind.Gaming => Gaming,
        ProfileKind.Privacy => Privacy(catalog),
        _ => new TweakProfile(ProfileKind.ResetAll, Array.Empty<string>()),
    };
}
