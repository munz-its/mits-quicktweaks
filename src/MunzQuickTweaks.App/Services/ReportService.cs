using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.Core.Tools;
using MunzQuickTweaks.Core.Tweaks;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.Services;

/// <summary>
/// PC-Bericht als HTML: sammelt Systemdaten, Gesundheit, Treiber, aktive Tweaks und Benchmark,
/// speichert die Datei auf dem Desktop und öffnet sie im Browser. Jeder Teil ist einzeln abgesichert.
/// </summary>
public sealed class ReportService
{
    private readonly SystemInfoProvider _system;
    private readonly TweakService _tweaks;
    private readonly BenchmarkStore _benchmarks;
    private readonly UpdateService _app;
    private readonly LocalizationService _localization;
    private readonly ILogger<ReportService> _log;

    public ReportService(SystemInfoProvider system, TweakService tweaks, BenchmarkStore benchmarks, UpdateService app,
        LocalizationService localization, ILogger<ReportService> log)
    {
        _system = system;
        _tweaks = tweaks;
        _benchmarks = benchmarks;
        _app = app;
        _localization = localization;
        _log = log;
    }

    /// <summary>Erstellt den Bericht und öffnet ihn. Rückgabe: Pfad der Datei.</summary>
    public async Task<string> CreateAndOpenAsync()
    {
        var path = await Task.Run(Create);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.LogWarning(ex, "Bericht nicht geöffnet");
        }

        return path;
    }

    private string Create()
    {
        var lang = _localization.CurrentLanguage;
        var culture = CultureInfo.CurrentCulture;
        var history = Safe(() => _benchmarks.All(), Array.Empty<BenchmarkResult>(), "Benchmark");
        var latest = history.OrderByDescending(b => b.Time).ToList();
        var boots = Safe(() => WindowsBootTimes.Read(5), Array.Empty<BootRecord>(), "Startzeiten");

        var data = new PcReportData
        {
            ComputerName = Environment.MachineName,
            Created = DateTimeOffset.Now,
            AppVersion = _app.CurrentVersion,
            System = Safe(() => _system.Read(), null, "Systemdaten"),
            Disks = Safe(WindowsHealth.Disks, Array.Empty<DiskHealth>(), "Datenträger"),
            Battery = Safe(WindowsHealth.Battery, null, "Akku"),
            Drivers = Safe(() => DriverAdvisor.Relevant(WindowsDrivers.List()), Array.Empty<DriverInfo>(), "Treiber"),
            Tweaks = Safe(() => _tweaks.Tweaks
                    .Where(t => _tweaks.GetStatus(t) == TweakStatus.Applied)
                    .OrderBy(t => t.Category)
                    .Select(t => (Category(t.Category), t.Title.Get(lang)))
                    .ToList(),
                new List<(string, string)>(), "Tweaks"),
            Benchmark = latest.FirstOrDefault(),
            PreviousBenchmark = latest.Skip(1).FirstOrDefault(),
            BootSeconds = boots.Count > 0 ? Math.Round(boots.Average(b => b.MainPathMs) / 1000.0, 1) : null,
        };

        var html = PcReport.Build(data, _localization.Get, culture);
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            folder = Path.GetTempPath();
        }

        var path = Path.Combine(folder, PcReport.FileName(_localization.Get("Report.FilePrefix"), data.ComputerName, data.Created));
        File.WriteAllText(path, html, new System.Text.UTF8Encoding(false));
        _log.LogInformation("PC-Bericht erstellt: {Path}", path);
        return path;
    }

    private string Category(TweakCategory category)
    {
        var text = _localization.Get($"Cat.{category}");
        return text.Length > 1 ? text[0] + text[1..].ToLower(CultureInfo.CurrentCulture) : text;
    }

    private T Safe<T>(Func<T> read, T fallback, string what)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bericht: {What} nicht lesbar", what);
            return fallback;
        }
    }
}
