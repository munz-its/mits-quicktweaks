using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

public sealed record HealthRow(string Title, string Detail, string LevelText, HealthLevel Level)
{
    public bool IsGood => Level == HealthLevel.Good;

    public bool IsWarning => Level == HealthLevel.Warning;

    public bool IsBad => Level == HealthLevel.Bad;
}

/// <summary>Zustand von SSD/HDD (SMART) und Akku, dazu Windows-Reparatur.</summary>
public sealed partial class HealthViewModel : PageViewModel
{
    private readonly Windows.WindowsCleanup _cleanup;
    private readonly LocalizationService _localization;
    private readonly ILogger<HealthViewModel> _log;

    public HealthViewModel(Windows.WindowsCleanup cleanup, LocalizationService localization, ILogger<HealthViewModel> log)
        : base("Nav.Health")
    {
        _cleanup = cleanup;
        _localization = localization;
        _log = log;
    }

    public ObservableCollection<HealthRow> Disks { get; } = new();

    [ObservableProperty]
    private HealthRow? _battery;

    public bool HasBattery => Battery is not null;

    partial void OnBatteryChanged(HealthRow? value) => OnPropertyChanged(nameof(HasBattery));

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _status = string.Empty;

    public override async Task OnNavigatedToAsync()
    {
        IsLoading = true;
        try
        {
            var (disks, battery) = await Task.Run(() =>
            {
                IReadOnlyList<DiskHealth> d;
                BatteryHealth? b;
                try
                {
                    d = Windows.WindowsHealth.Disks();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "SMART nicht lesbar");
                    d = Array.Empty<DiskHealth>();
                }

                try
                {
                    b = Windows.WindowsHealth.Battery();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Akku nicht lesbar");
                    b = null;
                }

                return (d, b);
            });

            Disks.Clear();
            foreach (var disk in disks)
            {
                var parts = new List<string> { disk.Media, DiskAnalyzer.FormatSize(disk.SizeBytes) };
                if (disk.WearPercent is { } wear) parts.Add(string.Format(CultureInfo.CurrentCulture, _localization.Get("Health.Wear"), wear));
                if (disk.TemperatureC is { } temp) parts.Add($"{temp} °C");
                if (disk.PowerOnHours is { } hours) parts.Add(string.Format(CultureInfo.CurrentCulture, _localization.Get("Health.Hours"), hours.ToString("N0", CultureInfo.CurrentCulture)));
                if (disk.ReadErrors is > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, _localization.Get("Health.ReadErrors"), disk.ReadErrors));
                var level = HealthAssessment.Disk(disk);
                Disks.Add(new HealthRow(disk.Name, string.Join(" · ", parts), _localization.Get($"Health.Level.{level}"), level));
            }

            if (battery is not null)
            {
                var percent = HealthAssessment.BatteryPercent(battery);
                var level = HealthAssessment.Battery(battery);
                var detail = string.Format(CultureInfo.CurrentCulture, _localization.Get("Health.BatteryDetail"),
                    percent?.ToString(CultureInfo.CurrentCulture) ?? "?", battery.FullCapacity.ToString("N0", CultureInfo.CurrentCulture),
                    battery.DesignCapacity.ToString("N0", CultureInfo.CurrentCulture));
                if (battery.Cycles is { } cycles)
                {
                    detail += " · " + string.Format(CultureInfo.CurrentCulture, _localization.Get("Health.Cycles"), cycles);
                }

                Battery = new HealthRow(_localization.Get("Health.Battery"), detail, _localization.Get($"Health.Level.{level}"), level);
            }
            else
            {
                Battery = null;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RunSfc() => _cleanup.StartRepair(Windows.RepairTool.Sfc);

    [RelayCommand]
    private void RunDism() => _cleanup.StartRepair(Windows.RepairTool.Dism);

    [RelayCommand]
    private async Task BatteryReportAsync()
    {
        var file = await Task.Run(Windows.WindowsHealth.CreateBatteryReport);
        if (file is null)
        {
            Status = _localization.Get("Tweak.Failed");
            return;
        }

        Status = _localization.Get("Health.ReportSaved");
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Kein Browser
        }
    }
}
