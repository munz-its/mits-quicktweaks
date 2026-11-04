using System.Text.Json;
using System.Text.Json.Serialization;

namespace MunzQuickTweaks.Core.Safety;

public enum HistoryAction
{
    Applied,
    Reverted,
    RestorePointCreated,
    RestorePointSkipped,
    ProtectionEnabled,
    RegistryExported,
    AppRemoved,
    StartupEnabled,
    StartupDisabled,
    CleanupDone,
    ProfileApplied,
}

public sealed record HistoryEntry(DateTimeOffset Time, HistoryAction Action, string? TweakId, bool Success, string? Message);

/// <summary>
/// Änderungsprotokoll als JSON-Zeilen (eine Zeile pro Ereignis). Anhängen ist robust:
/// eine kaputte Zeile verliert nur sich selbst, nie das ganze Protokoll.
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly TimeProvider _time;
    private readonly object _lock = new();

    public HistoryStore(string filePath, TimeProvider? time = null)
    {
        _filePath = filePath;
        _time = time ?? TimeProvider.System;
    }

    public void Add(HistoryAction action, string? tweakId, bool success, string? message = null)
    {
        var entry = new HistoryEntry(_time.GetLocalNow(), action, tweakId, success, message);
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_filePath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Protokoll ist Komfort, darf aber nie einen Tweak blockieren
            }
        }
    }

    /// <summary>Neueste zuerst.</summary>
    public IReadOnlyList<HistoryEntry> Latest(int count)
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<HistoryEntry>();
            }

            var result = new List<HistoryEntry>();
            foreach (var line in File.ReadLines(_filePath).Reverse())
            {
                if (result.Count >= count)
                {
                    break;
                }

                try
                {
                    if (JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions) is { } entry)
                    {
                        result.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Einzelne beschädigte Zeile überspringen
                }
            }

            return result;
        }
    }
}
