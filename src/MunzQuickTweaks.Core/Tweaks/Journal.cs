using System.Text.Json;
using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Tweaks;

/// <summary>Zustand vor der Änderung, pro Aktion. Grundlage für exaktes Rückgängig.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistrySnapshot), "registry")]
[JsonDerivedType(typeof(PowerPlanSnapshot), "powerPlan")]
[JsonDerivedType(typeof(JsonSettingSnapshot), "jsonSetting")]
public abstract class ActionSnapshot
{
}

public sealed class RegistrySnapshot : ActionSnapshot
{
    public RegistryHive Hive { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool KeyExisted { get; set; }

    /// <summary>Null = Wert existierte vorher nicht.</summary>
    public RegistryValue? Previous { get; set; }
}

public sealed class PowerPlanSnapshot : ActionSnapshot
{
    public Guid PreviousActive { get; set; }

    /// <summary>Von der App erzeugter Plan, wird beim Rückgängig gelöscht. Null = Plan existierte schon.</summary>
    public Guid? Created { get; set; }
}

public sealed class JsonSettingSnapshot : ActionSnapshot
{
    public string File { get; set; } = string.Empty;

    public string Property { get; set; } = string.Empty;

    /// <summary>Vorheriger Wert als JSON-Text. Null = Einstellung existierte nicht.</summary>
    public string? Previous { get; set; }
}

public sealed class JournalEntry
{
    public string TweakId { get; set; } = string.Empty;

    public DateTimeOffset AppliedAt { get; set; }

    public List<ActionSnapshot> Snapshots { get; set; } = new();
}

/// <summary>
/// Speichert pro angewendetem Tweak den Originalzustand.
/// Liegt unter %ProgramData% und überlebt dadurch auch eine neue Version der App.
/// </summary>
public sealed class JournalStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, JournalEntry>? _entries;

    public JournalStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyCollection<JournalEntry> All
    {
        get
        {
            lock (_lock)
            {
                return Entries.Values.ToList();
            }
        }
    }

    private Dictionary<string, JournalEntry> Entries => _entries ??= Load();

    public JournalEntry? Get(string tweakId)
    {
        lock (_lock)
        {
            return Entries.GetValueOrDefault(tweakId);
        }
    }

    public void Set(JournalEntry entry)
    {
        lock (_lock)
        {
            Entries[entry.TweakId] = entry;
            Save();
        }
    }

    public void Remove(string tweakId)
    {
        lock (_lock)
        {
            if (Entries.Remove(tweakId))
            {
                Save();
            }
        }
    }

    private Dictionary<string, JournalEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, JournalEntry>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(_filePath), JsonOptions) ?? new();
            return list.ToDictionary(e => e.TweakId);
        }
        catch (JsonException)
        {
            // Beschädigtes Journal nicht überschreiben, sondern sichern: es enthält Originalwerte
            File.Copy(_filePath, _filePath + $".defekt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return new Dictionary<string, JournalEntry>();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Entries.Values.OrderBy(e => e.TweakId).ToList(), JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
