namespace MunzQuickTweaks.Core.Machine;

/// <summary>Momentaufnahme der wichtigsten Systemdaten für das Dashboard.</summary>
public sealed record SystemInfo(
    string OperatingSystem,
    int Build,
    bool IsSupported,
    string Processor,
    IReadOnlyList<string> GraphicsCards,
    double MemoryGb,
    bool IsAdministrator);
