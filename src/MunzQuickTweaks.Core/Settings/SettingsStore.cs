using System.Text.Json;
using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Settings;

/// <summary>
/// Liest und schreibt die Einstellungen. Eine kaputte oder fehlende Datei
/// führt nie zu einem Absturz, sondern zu Standardwerten.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public SettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Erst in eine Temp-Datei schreiben, dann ersetzen: kein halber Inhalt bei Absturz
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
