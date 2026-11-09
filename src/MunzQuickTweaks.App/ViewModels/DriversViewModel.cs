using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

public sealed record DriverRow(string Group, string Device, string Manufacturer, string Version, string Date, bool IsOld, string Link);

/// <summary>Wichtige Treiber mit Alter und Link zur Herstellerseite. Installiert selbst nichts.</summary>
public sealed partial class DriversViewModel : PageViewModel
{
    private readonly LocalizationService _localization;
    private readonly ILogger<DriversViewModel> _log;

    public DriversViewModel(LocalizationService localization, ILogger<DriversViewModel> log)
        : base("Nav.Drivers")
    {
        _localization = localization;
        _log = log;
    }

    public ObservableCollection<DriverRow> Drivers { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summary = string.Empty;

    public override async Task OnNavigatedToAsync()
    {
        if (Drivers.Count > 0 || IsLoading)
        {
            return;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var drivers = await Task.Run(() => DriverAdvisor.Relevant(Windows.WindowsDrivers.List()));
            var today = DateTime.Today;
            Drivers.Clear();
            foreach (var d in drivers)
            {
                var group = _localization.Get("Drivers.Class." + d.DeviceClass.ToUpperInvariant());
                Drivers.Add(new DriverRow(group, d.Device, d.Manufacturer, d.Version,
                    d.Date?.ToString("d", CultureInfo.CurrentCulture) ?? "-",
                    DriverAdvisor.IsOld(d, today), DriverAdvisor.LinkFor(d.Manufacturer)));
            }

            var old = Drivers.Count(d => d.IsOld);
            Summary = string.Format(CultureInfo.CurrentCulture, _localization.Get("Drivers.Summary"), Drivers.Count, old);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _log.LogError(ex, "Treiber nicht lesbar");
            Summary = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private static void OpenLink(string link) => Open(link);

    [RelayCommand]
    private static void OpenWindowsUpdate() => Open(DriverAdvisor.WindowsUpdateOptional);

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Kein Browser oder Einstellungen nicht verfügbar
        }
    }
}
