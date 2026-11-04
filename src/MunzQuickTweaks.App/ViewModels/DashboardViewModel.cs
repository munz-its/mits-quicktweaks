using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Startseite mit Systemdaten. Die Daten werden einmal im Hintergrund gelesen.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly SystemInfoProvider _provider;
    private bool _loaded;

    public DashboardViewModel(SystemInfoProvider provider)
        : base("Nav.Dashboard")
    {
        _provider = provider;
    }

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
