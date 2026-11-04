using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Startseite mit Systemdaten. Die Daten werden einmal im Hintergrund gelesen.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly SystemInfoProvider _provider;
    private readonly TweakService _tweaks;
    private bool _loaded;

    public DashboardViewModel(SystemInfoProvider provider, TweakService tweaks)
        : base("Nav.Dashboard")
    {
        _provider = provider;
        _tweaks = tweaks;
    }

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
        ActiveTweaks = await Task.Run(_tweaks.CountApplied);

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
