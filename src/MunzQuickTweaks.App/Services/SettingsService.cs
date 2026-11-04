using MunzQuickTweaks.Core.Settings;

namespace MunzQuickTweaks.App.Services;

/// <summary>Hält die geladenen Einstellungen und speichert Änderungen sofort.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            _store.Save(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Einstellungen sind nicht kritisch: Fehler protokollieren, App läuft weiter
            CrashLog.Write("Einstellungen konnten nicht gespeichert werden", ex);
        }
    }
}
