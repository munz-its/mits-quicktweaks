namespace MunzQuickTweaks.Core.Modules;

/// <summary>Energiemodus wie unter Einstellungen > System > Netzbetrieb und Akku.</summary>
public enum PowerMode
{
    Efficiency,
    Balanced,
    Performance,
}

public sealed record PowerPlanInfo(Guid Id, string Name, bool IsActive);

/// <summary>Energiemodus (Overlay) und Energiepläne.</summary>
public interface IPowerModes
{
    /// <summary>Windows wertet den Energiemodus nur mit dem Plan "Ausbalanciert" aus.</summary>
    bool ModeAvailable { get; }

    /// <summary>Null, wenn ein Modus aktiv ist, den die App nicht kennt.</summary>
    PowerMode? GetMode();

    void SetMode(PowerMode mode);

    IReadOnlyList<PowerPlanInfo> ListPlans();

    void ActivatePlan(Guid id);
}

public static class PowerModeIds
{
    public static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid ToGuid(PowerMode mode) => mode switch
    {
        PowerMode.Efficiency => Efficiency,
        PowerMode.Performance => Performance,
        _ => Balanced,
    };

    public static PowerMode? FromGuid(Guid id) =>
        id == Efficiency ? PowerMode.Efficiency
        : id == Balanced ? PowerMode.Balanced
        : id == Performance ? PowerMode.Performance
        : null;
}
