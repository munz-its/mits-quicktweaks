using System.Text.Json;
using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Tweaks;

/// <summary>
/// Exportierte Tweak-Auswahl, um dieselben Tweaks auf mehreren PCs anzuwenden.
/// Enthält nur Tweak-IDs, keine persönlichen Daten.
/// </summary>
public sealed class TweakSelection
{
    public const string FormatName = "munz-quicktweaks-selection";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Format { get; set; } = FormatName;

    public int FormatVersion { get; set; } = 1;

    public string AppVersion { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public List<string> Tweaks { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Liest eine Datei. Fremde oder kaputte Dateien ergeben eine verständliche Ausnahme.</summary>
    public static TweakSelection Parse(string json)
    {
        TweakSelection? selection;
        try
        {
            selection = JsonSerializer.Deserialize<TweakSelection>(json, Options);
        }
        catch (JsonException)
        {
            throw new FormatException("Die Datei ist keine gültige Tweak-Auswahl.");
        }

        if (selection is null || selection.Format != FormatName)
        {
            throw new FormatException("Die Datei stammt nicht von Munz Quick Tweaks.");
        }

        if (selection.FormatVersion > 1)
        {
            throw new FormatException("Die Datei stammt aus einer neueren Version von Munz Quick Tweaks.");
        }

        selection.Tweaks = selection.Tweaks.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        return selection;
    }
}
