using System.Reflection;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Core.Tweaks;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.Services;

/// <summary>Lädt den eingebetteten Katalog und stellt die Engine für die Oberfläche bereit.</summary>
public sealed class TweakService
{
    private const string CatalogPrefix = "catalog.";

    private readonly Lazy<(IReadOnlyList<TweakDefinition> Tweaks, string? Error)> _catalog;

    public TweakService(SystemInfoProvider systemInfo)
    {
        Engine = new TweakEngine(
            new WindowsRegistry(),
            new JournalStore(Path.Combine(AppPaths.MachineData, "state", "journal.json")),
            new WindowsNotifier(),
            systemInfo.ReadBuild());

        _catalog = new Lazy<(IReadOnlyList<TweakDefinition>, string?)>(LoadCatalog);
    }

    public TweakEngine Engine { get; }

    public IReadOnlyList<TweakDefinition> Tweaks => _catalog.Value.Tweaks;

    /// <summary>Fehlermeldung, falls der Katalog nicht geladen werden konnte.</summary>
    public string? CatalogError => _catalog.Value.Error;

    public event EventHandler? Changed;

    public TweakStatus GetStatus(TweakDefinition tweak)
    {
        try
        {
            return Engine.GetStatus(tweak);
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Status von {tweak.Id} nicht lesbar", ex);
            return TweakStatus.NotAvailable;
        }
    }

    public async Task<TweakResult> ApplyAsync(TweakDefinition tweak)
    {
        var result = await Task.Run(() => Engine.Apply(tweak));
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public async Task<TweakResult> RevertAsync(TweakDefinition tweak)
    {
        var result = await Task.Run(() => Engine.Revert(tweak));
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Anzahl der Tweaks, die gerade wirklich aktiv sind.</summary>
    public int CountApplied() => Tweaks.Count(t => GetStatus(t) == TweakStatus.Applied);

    private static (IReadOnlyList<TweakDefinition>, string?) LoadCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var streams = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(CatalogPrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => (n, assembly.GetManifestResourceStream(n)!))
            .ToList();

        try
        {
            return (CatalogLoader.Load(streams), null);
        }
        catch (CatalogException ex)
        {
            CrashLog.Write("Tweak-Katalog ungültig", ex);
            return (Array.Empty<TweakDefinition>(), ex.Message);
        }
        finally
        {
            foreach (var (_, stream) in streams)
            {
                stream.Dispose();
            }
        }
    }
}
