using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Core.Modules;

public enum ProfileKind
{
    Recommended,
    Gaming,
    Privacy,
    Telemetry,
    Battery,
    Office,
    MaxPerformance,
    ResetAll,
}

/// <summary>
/// Ein Klick für mehrere Tweaks. Inhalte am 04.11.2026 mit Mert abgestimmt, neue Profile am 10.11.2026.
/// Zusätze außerhalb der Tweaks: Energiemodus, Autostart von Programmen (Kennungen aus AppCatalog) und Debloat-Gruppen.
/// </summary>
public sealed record TweakProfile(ProfileKind Kind, IReadOnlyList<string> TweakIds)
{
    public PowerMode? Mode { get; init; }

    public IReadOnlyList<string> StartupApps { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DebloatGroup> Debloat { get; init; } = Array.Empty<DebloatGroup>();
}

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

    public static readonly TweakProfile Telemetry = new(ProfileKind.Telemetry, new[]
    {
        "privacy.telemetry.min", "services.diagtrack.off", "services.wer.off", "privacy.feedback.off", "privacy.apptracking.off",
        "privacy.inking.off", "privacy.tailored.off", "apps.chrome.metrics.off", "apps.edge.personalization.off",
    });

    public static readonly TweakProfile Battery = new(ProfileKind.Battery, new[]
    {
        "perf.bgapps.off", "perf.animations.reduce", "perf.transparency.off",
        "apps.edge.startupboost.off", "apps.edge.background.off", "apps.chrome.memorysaver.on",
    })
    {
        Mode = PowerMode.Efficiency,
        StartupApps = new[] { "steam", "epic", "discord", "edge" },
        Debloat = new[] { DebloatGroup.Ads },
    };

    public static readonly TweakProfile Office = new(ProfileKind.Office, new[]
    {
        "ui.fileext.show", "ui.explorer.thispc", "ui.widgets.off", "privacy.tips.off", "privacy.lockscreen.ads",
        "privacy.websearch.off", "perf.startupdelay.off", "ui.endtask.on",
    })
    {
        StartupApps = new[] { "steam", "epic", "discord" },
        Debloat = new[] { DebloatGroup.Ads, DebloatGroup.Xbox },
    };

    /// <summary>Gaming plus mehr. Xbox-Apps bleiben bewusst, Game Bar und Xbox-App brauchen viele Spiele.</summary>
    public static readonly TweakProfile MaxPerformance = new(ProfileKind.MaxPerformance,
        Gaming.TweakIds.Concat(new[]
        {
            "gaming.timerres.global", "gaming.powerthrottling.off", "perf.bgapps.off", "perf.animations.reduce",
            "perf.transparency.off", "net.throttling.off",
        }).Distinct().ToList())
    {
        Mode = PowerMode.Performance,
        StartupApps = new[] { "edge", "chrome", "discord", "steam", "epic" },
        Debloat = new[] { DebloatGroup.Ads, DebloatGroup.MicrosoftExtras },
    };

    public static TweakProfile Get(ProfileKind kind, IEnumerable<TweakDefinition> catalog) => kind switch
    {
        ProfileKind.Recommended => Recommended,
        ProfileKind.Gaming => Gaming,
        ProfileKind.Privacy => Privacy(catalog),
        ProfileKind.Telemetry => Telemetry,
        ProfileKind.Battery => Battery,
        ProfileKind.Office => Office,
        ProfileKind.MaxPerformance => MaxPerformance,
        _ => new TweakProfile(ProfileKind.ResetAll, Array.Empty<string>()),
    };
}
