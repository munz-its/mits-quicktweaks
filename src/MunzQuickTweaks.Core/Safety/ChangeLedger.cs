using System.Text.Json;

namespace MunzQuickTweaks.Core.Safety;

/// <summary>Ursprünglicher Wert einer Prozessregel. Null-Werte = gab es vorher nicht.</summary>
public sealed record ProcessOriginal(int? Cpu, int? Io);

/// <summary>Ursprungszustand von allem, was die App außerhalb der Tweaks ändert. Pro Sache zählt nur der erste Stand.</summary>
public sealed class LedgerData
{
    public Guid? OriginalPlan { get; set; }

    public Guid? OriginalOverlay { get; set; }

    /// <summary>Autostart-Eintrag (Id) und ob er ursprünglich an war.</summary>
    public Dictionary<string, bool> Startup { get; set; } = new();

    /// <summary>Entfernte Apps (Paketname).</summary>
    public List<string> RemovedApps { get; set; } = new();

    /// <summary>Prozessregel (exe) und ihr ursprünglicher Wert.</summary>
    public Dictionary<string, ProcessOriginal> Processes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool OneDriveUninstalled { get; set; }

    /// <summary>Grafikkarten-Wahl pro Programm: Pfad und vorheriger Rohwert (null = gab es nicht).</summary>
    public Dictionary<string, string?> Gpu { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>DNS pro Adapter (GUID): vorherige Server, leer = automatisch.</summary>
    public Dictionary<string, List<string>> Dns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => OriginalPlan is null && OriginalOverlay is null && Startup.Count == 0 && RemovedApps.Count == 0
                           && Processes.Count == 0 && !OneDriveUninstalled && Gpu.Count == 0 && Dns.Count == 0;
}

/// <summary>
/// Änderungsbuch für "Alles zurücksetzen". Liegt neben dem Journal und überlebt Neustarts.
/// </summary>
public sealed class ChangeLedger
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _path;
    private readonly object _lock = new();

    public ChangeLedger(string path)
    {
        _path = path;
    }

    public LedgerData Data
    {
        get
        {
            lock (_lock)
            {
                return Load();
            }
        }
    }

    public void RecordPower(Guid plan, Guid? overlay) => Update(d =>
    {
        d.OriginalPlan ??= plan;
        d.OriginalOverlay ??= overlay;
    });

    public void RecordStartup(string id, bool originallyEnabled) => Update(d => d.Startup.TryAdd(id, originallyEnabled));

    public void RecordAppRemoved(string package) => Update(d =>
    {
        if (!d.RemovedApps.Contains(package, StringComparer.OrdinalIgnoreCase))
        {
            d.RemovedApps.Add(package);
        }
    });

    public void RecordAppReinstalled(string package) => Update(d => d.RemovedApps.RemoveAll(p => string.Equals(p, package, StringComparison.OrdinalIgnoreCase)));

    public void RecordProcess(string exe, ProcessOriginal original) => Update(d => d.Processes.TryAdd(exe, original));

    public void RecordOneDrive(bool uninstalled) => Update(d => d.OneDriveUninstalled = uninstalled);

    public void RecordGpu(string exePath, string? original) => Update(d => d.Gpu.TryAdd(exePath, original));

    public void RecordDns(string adapterGuid, IReadOnlyList<string> original) => Update(d => d.Dns.TryAdd(adapterGuid, original.ToList()));

    public void ClearGpu() => Update(d => d.Gpu.Clear());

    public void ClearDns() => Update(d => d.Dns.Clear());

    /// <summary>Nach dem Zurücksetzen: erledigte Bereiche leeren.</summary>
    public void Clear(bool power, bool startup, bool apps, bool processes, bool oneDrive) => Update(d =>
    {
        if (power)
        {
            d.OriginalPlan = null;
            d.OriginalOverlay = null;
        }

        if (startup) d.Startup.Clear();
        if (apps) d.RemovedApps.Clear();
        if (processes) d.Processes.Clear();
        if (oneDrive) d.OneDriveUninstalled = false;
    });

    private void Update(Action<LedgerData> change)
    {
        lock (_lock)
        {
            var data = Load();
            change(data);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(data, Options));
        }
    }

    private LedgerData Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new LedgerData();
            }

            var data = JsonSerializer.Deserialize<LedgerData>(File.ReadAllText(_path), Options) ?? new LedgerData();
            data.Processes = new Dictionary<string, ProcessOriginal>(data.Processes, StringComparer.OrdinalIgnoreCase);
            data.Gpu = new Dictionary<string, string?>(data.Gpu, StringComparer.OrdinalIgnoreCase);
            data.Dns = new Dictionary<string, List<string>>(data.Dns, StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new LedgerData();
        }
    }
}
