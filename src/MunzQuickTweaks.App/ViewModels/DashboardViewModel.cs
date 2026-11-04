using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Startseite mit Systemdaten. Die Daten werden einmal im Hintergrund gelesen.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly SystemInfoProvider _provider;
    private readonly TweakService _tweaks;
    private readonly IRestorePointService _restorePoints;
    private bool _loaded;

    public DashboardViewModel(SystemInfoProvider provider, TweakService tweaks, IRestorePointService restorePoints)
        : base("Nav.Dashboard")
    {
        _provider = provider;
        _tweaks = tweaks;
        _restorePoints = restorePoints;
    }

    /// <summary>Datum des neuesten Wiederherstellungspunkts, leer = keiner vorhanden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestorePoint))]
    private string _lastRestorePoint = string.Empty;

    public bool HasRestorePoint => LastRestorePoint.Length > 0;

    [ObservableProperty]
    private int _activeTweaks;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _operatingSystem = string.Empty;

    [ObservableProperty]
    private string _processor = string.Empty;

    [ObservableProperty]
    private string _graphics = string.Empty;

    [ObservableProperty]
    private string _memory = string.Empty;

    [ObservableProperty]
    private bool _isAdministrator;

    [ObservableProperty]
    private bool _isUnsupported;

    public override async Task OnNavigatedToAsync()
    {
        // Zähler bei jedem Öffnen aktualisieren, Systemdaten nur einmal lesen
        var (count, last) = await Task.Run(() => (_tweaks.CountApplied(), _restorePoints.List().FirstOrDefault()));
        ActiveTweaks = count;
        LastRestorePoint = last is null ? string.Empty : last.Created.ToString("g", CultureInfo.CurrentUICulture);

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var info = await Task.Run(_provider.Read);
            OperatingSystem = info.OperatingSystem;
            Processor = info.Processor;
            Graphics = info.GraphicsCards.Count > 0 ? string.Join(Environment.NewLine, info.GraphicsCards) : "?";
            Memory = info.MemoryGb > 0 ? info.MemoryGb.ToString("0.#", CultureInfo.CurrentCulture) + " GB" : "?";
            IsAdministrator = info.IsAdministrator;
            IsUnsupported = !info.IsSupported;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Systemdaten konnten nicht gelesen werden", ex);
            OperatingSystem = Processor = Graphics = Memory = "?";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
