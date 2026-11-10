using System.Text.Json;

namespace MunzQuickTweaks.Core.Safety;

/// <summary>Merkt sich, welche zurückgesetzten Tweaks schon gemeldet wurden, damit der Hinweis nicht jeden Tag erscheint.</summary>
public sealed class ResetNotice
{
    private readonly string _path;

    public ResetNotice(string path)
    {
        _path = path;
    }

    /// <summary>True, wenn sich die Liste seit dem letzten Hinweis geändert hat.</summary>
    public bool ShouldNotify(IEnumerable<string> tweakIds)
    {
        var now = tweakIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        return now.Count > 0 && !now.SequenceEqual(Load());
    }

    public void Remember(IEnumerable<string> tweakIds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(tweakIds.OrderBy(id => id, StringComparer.Ordinal).ToList()));
    }

    private List<string> Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new();
        }
    }
}
