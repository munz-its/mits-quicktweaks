namespace MunzQuickTweaks.Core.Machine;

/// <summary>
/// Übersetzt Rohwerte aus der Registry in lesbare Namen.
/// Windows 11 meldet in ProductName weiterhin "Windows 10", daher zählt die Build-Nummer.
/// </summary>
public static class OsNaming
{
    /// <summary>Erster Build von Windows 11.</summary>
    public const int FirstWindows11Build = 22000;

    /// <summary>Windows 10 22H2, die einzige unterstützte Windows-10-Version.</summary>
    public const int Windows10_22H2Build = 19045;

    public static bool IsWindows11(int build) => build >= FirstWindows11Build;

    /// <summary>Unterstützt sind Windows 10 22H2 und alle Windows-11-Versionen.</summary>
    public static bool IsSupported(int build) => build == Windows10_22H2Build || IsWindows11(build);

    public static string GetEditionName(string? editionId) => editionId switch
    {
        "Core" or "CoreSingleLanguage" or "CoreCountrySpecific" => "Home",
        "Professional" => "Pro",
        "ProfessionalWorkstation" => "Pro for Workstations",
        "ProfessionalEducation" => "Pro Education",
        "Education" => "Education",
        "Enterprise" => "Enterprise",
        "EnterpriseS" => "Enterprise LTSC",
        "IoTEnterpriseS" => "IoT Enterprise LTSC",
        null or "" => "",
        _ => editionId,
    };

    /// <summary>Beispiel: "Windows 11 Home 24H2 (Build 26100.2314)".</summary>
    public static string GetDisplayName(int build, int revision, string? editionId, string? displayVersion)
    {
        var parts = new List<string> { IsWindows11(build) ? "Windows 11" : "Windows 10" };

        var edition = GetEditionName(editionId);
        if (edition.Length > 0)
        {
            parts.Add(edition);
        }

        if (!string.IsNullOrWhiteSpace(displayVersion))
        {
            parts.Add(displayVersion);
        }

        return $"{string.Join(' ', parts)} (Build {build}.{revision})";
    }
}
