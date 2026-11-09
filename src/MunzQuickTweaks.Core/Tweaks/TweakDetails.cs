namespace MunzQuickTweaks.Core.Tweaks;

/// <summary>
/// Technische Details für "Mehr Info": genau welche Einstellung der Tweak ändert.
/// Bewusst offen, damit Nutzer und Support jeden Schritt nachvollziehen können.
/// </summary>
public static class TweakDetails
{
    public static IReadOnlyList<string> Describe(TweakDefinition tweak, string language)
    {
        var de = language == "de";
        var lines = new List<string>();

        foreach (var action in tweak.Actions)
        {
            switch (action)
            {
                case RegistryAction r:
                    var name = r.Name.Length == 0 ? (de ? "(Standard)" : "(Default)") : r.Name;
                    var value = r.Kind is RegistryKind.String or RegistryKind.ExpandString ? $"\"{r.Value}\"" : r.Value;
                    var standard = r.Default is null
                        ? (de ? "Windows-Standard: Wert fehlt" : "Windows default: value absent")
                        : (de ? $"Windows-Standard: {r.Default}" : $"Windows default: {r.Default}");
                    lines.Add($"{r.Hive}\\{r.Key}\\{name} = {value} ({r.Kind}) · {standard}");
                    break;

                case ServiceAction s:
                    lines.Add(de
                        ? $"Dienst {s.Name}: Starttyp {StartName(s.StartType, true)} · Windows-Standard: {StartName(s.Default, true)}"
                        : $"Service {s.Name}: start type {StartName(s.StartType, false)} · Windows default: {StartName(s.Default, false)}");
                    break;

                case PowerPlanAction p:
                    lines.Add(de
                        ? $"Energieplan {p.Template} aktivieren, fehlt er, wird er aus der Vorlage erzeugt"
                        : $"Activate power plan {p.Template}, created from the template if missing");
                    break;

                default:
                    lines.Add(action.GetType().Name);
                    break;
            }
        }

        var build = tweak.MaxBuild == 0
            ? (de ? $"Ab Windows-Build {tweak.MinBuild}" : $"From Windows build {tweak.MinBuild}")
            : (de ? $"Windows-Build {tweak.MinBuild} bis {tweak.MaxBuild}" : $"Windows build {tweak.MinBuild} to {tweak.MaxBuild}");
        lines.Add(build);
        lines.Add(de ? $"ID: {tweak.Id}" : $"ID: {tweak.Id}");
        return lines;
    }

    private static string StartName(ServiceStartType type, bool de) => type switch
    {
        ServiceStartType.Automatic => de ? "Automatisch" : "Automatic",
        ServiceStartType.AutomaticDelayed => de ? "Automatisch (verzögert)" : "Automatic (delayed)",
        ServiceStartType.Manual => de ? "Manuell" : "Manual",
        ServiceStartType.Disabled => de ? "Deaktiviert" : "Disabled",
        _ => type.ToString(),
    };
}
