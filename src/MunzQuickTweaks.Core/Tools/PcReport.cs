using System.Globalization;
using System.Net;
using System.Text;
using MunzQuickTweaks.Core.Machine;

namespace MunzQuickTweaks.Core.Tools;

/// <summary>Alle Daten für den PC-Bericht. Fehlende Teile bleiben null oder leer und erscheinen dann als "nicht verfügbar".</summary>
public sealed record PcReportData
{
    public required string ComputerName { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required string AppVersion { get; init; }

    public SystemInfo? System { get; init; }

    public IReadOnlyList<DiskHealth> Disks { get; init; } = Array.Empty<DiskHealth>();

    public BatteryHealth? Battery { get; init; }

    public IReadOnlyList<DriverInfo> Drivers { get; init; } = Array.Empty<DriverInfo>();

    /// <summary>Aktive Tweaks: Bereich und Titel, bereits übersetzt.</summary>
    public IReadOnlyList<(string Category, string Title)> Tweaks { get; init; } = Array.Empty<(string, string)>();

    public BenchmarkResult? Benchmark { get; init; }

    public BenchmarkResult? PreviousBenchmark { get; init; }

    /// <summary>Durchschnitt der letzten Starts bis zum Desktop, in Sekunden.</summary>
    public double? BootSeconds { get; init; }
}

/// <summary>
/// Baut den PC-Bericht als eigenständige HTML-Datei: ohne Skripte und ohne externe Inhalte, druckbar als PDF.
/// Texte kommen übersetzt über <c>label</c>, Schlüssel beginnen mit "Report.".
/// </summary>
public static class PcReport
{
    public static string Build(PcReportData data, Func<string, string> label, CultureInfo culture)
    {
        string L(string key) => label("Report." + key);
        var html = new StringBuilder();
        var title = $"{L("Title")} {data.ComputerName}";

        html.Append("<!DOCTYPE html>\n<html lang=\"").Append(culture.TwoLetterISOLanguageName).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(E(title)).Append("</title>\n<style>").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");

        html.Append("<header><div class=\"brand\">Munz Quick Tweaks</div><h1>").Append(E(title)).Append("</h1><p class=\"muted\">")
            .Append(E(string.Format(culture, L("Created"), data.Created.ToLocalTime().ToString("f", culture), data.AppVersion)))
            .Append("</p></header>\n");

        // System
        Section(html, L("System"));
        if (data.System is { } sys)
        {
            Table(html, new[]
            {
                (L("Os"), $"{sys.OperatingSystem} (Build {sys.Build})"),
                (L("Cpu"), sys.Processor),
                (L("Gpu"), sys.GraphicsCards.Count == 0 ? L("Unknown") : string.Join(", ", sys.GraphicsCards)),
                (L("Ram"), string.Format(culture, "{0:0.#} GB", sys.MemoryGb)),
            });
        }
        else
        {
            Missing(html, L);
        }

        // Datenträger
        Section(html, L("Disks"));
        if (data.Disks.Count > 0)
        {
            html.Append("<table class=\"grid\"><thead><tr><th>").Append(E(L("Disk"))).Append("</th><th>").Append(E(L("Size")))
                .Append("</th><th>").Append(E(L("State"))).Append("</th><th>").Append(E(L("Wear"))).Append("</th><th>")
                .Append(E(L("Temp"))).Append("</th><th>").Append(E(L("Hours"))).Append("</th></tr></thead><tbody>");
            foreach (var disk in data.Disks)
            {
                var level = HealthAssessment.Disk(disk);
                html.Append("<tr><td>").Append(E($"{disk.Name} ({disk.Media})")).Append("</td><td>")
                    .Append(E(string.Format(culture, "{0:N0} GB", disk.SizeBytes / 1_000_000_000.0))).Append("</td><td>")
                    .Append(Badge(level, L)).Append("</td><td>")
                    .Append(E(disk.WearPercent is { } w ? $"{w} %" : "–")).Append("</td><td>")
                    .Append(E(disk.TemperatureC is { } t ? $"{t} °C" : "–")).Append("</td><td>")
                    .Append(E(disk.PowerOnHours is { } h ? h.ToString("N0", culture) : "–")).Append("</td></tr>");
            }

            html.Append("</tbody></table>\n");
        }
        else
        {
            Missing(html, L);
        }

        // Akku nur bei Laptops
        if (data.Battery is { } battery)
        {
            Section(html, L("Battery"));
            var percent = HealthAssessment.BatteryPercent(battery);
            Table(html, new[]
            {
                (L("BatteryHealth"), percent is { } p ? $"{p} %" : L("Unknown")),
                (L("BatteryCapacity"), string.Format(culture, "{0:N0} / {1:N0} mWh", battery.FullCapacity, battery.DesignCapacity)),
                (L("BatteryCycles"), battery.Cycles?.ToString(culture) ?? "–"),
            }, Badge(HealthAssessment.Battery(battery), L));
        }

        // Leistung
        Section(html, L("Performance"));
        if (data.Benchmark is { } bench)
        {
            var before = data.PreviousBenchmark;
            html.Append("<table class=\"grid\"><thead><tr><th>").Append(E(L("Test"))).Append("</th><th>").Append(E(L("Result")))
                .Append("</th><th>").Append(E(L("Change"))).Append("</th></tr></thead><tbody>");
            Row(html, L("BenchCpuSingle"), bench.CpuSingle, before?.CpuSingle, "N0", "", culture);
            Row(html, L("BenchCpuMulti"), bench.CpuMulti, before?.CpuMulti, "N0", "", culture);
            Row(html, L("BenchRam"), bench.RamGBs, before?.RamGBs, "0.0", " GB/s", culture);
            Row(html, L("BenchDiskRead"), bench.DiskReadMBs, before?.DiskReadMBs, "N0", " MB/s", culture);
            Row(html, L("BenchDiskWrite"), bench.DiskWriteMBs, before?.DiskWriteMBs, "N0", " MB/s", culture);
            Row(html, L("BenchGpu"), bench.GpuFps, before?.GpuFps, "N0", " FPS", culture);
            html.Append("</tbody></table><p class=\"muted\">")
                .Append(E(string.Format(culture, L("BenchFrom"), bench.Time.ToLocalTime().ToString("g", culture))))
                .Append("</p>\n");
        }
        else
        {
            html.Append("<p class=\"muted\">").Append(E(L("NoBenchmark"))).Append("</p>\n");
        }

        if (data.BootSeconds is { } boot)
        {
            Table(html, new[] { (L("Boot"), string.Format(culture, "{0:0.0} s", boot)) });
        }

        // Tweaks
        Section(html, string.Format(culture, L("Tweaks"), data.Tweaks.Count));
        if (data.Tweaks.Count > 0)
        {
            foreach (var group in data.Tweaks.GroupBy(t => t.Category))
            {
                html.Append("<h3>").Append(E(group.Key)).Append("</h3><ul>");
                foreach (var tweak in group.OrderBy(t => t.Title, StringComparer.Create(culture, true)))
                {
                    html.Append("<li>").Append(E(tweak.Title)).Append("</li>");
                }

                html.Append("</ul>\n");
            }
        }
        else
        {
            html.Append("<p class=\"muted\">").Append(E(L("NoTweaks"))).Append("</p>\n");
        }

        // Treiber
        Section(html, L("Drivers"));
        if (data.Drivers.Count > 0)
        {
            var today = data.Created.LocalDateTime.Date;
            html.Append("<table class=\"grid\"><thead><tr><th>").Append(E(L("Device"))).Append("</th><th>").Append(E(L("Version")))
                .Append("</th><th>").Append(E(L("Date"))).Append("</th></tr></thead><tbody>");
            foreach (var driver in data.Drivers)
            {
                var old = DriverAdvisor.IsOld(driver, today);
                html.Append("<tr><td>").Append(E(driver.Device)).Append("<div class=\"muted small\">").Append(E(driver.Manufacturer)).Append("</div></td><td>")
                    .Append(E(driver.Version)).Append("</td><td>")
                    .Append(E(driver.Date?.ToString("d", culture) ?? "–"))
                    .Append(old ? $" <span class=\"badge warn\">{E(L("Old"))}</span>" : string.Empty)
                    .Append("</td></tr>");
            }

            html.Append("</tbody></table>\n");
        }
        else
        {
            Missing(html, L);
        }

        html.Append("<footer>").Append(E(L("Footer"))).Append("</footer>\n</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>Dateiname wie "PC-Bericht DESKTOP-123 2026-11-22.html", ohne verbotene Zeichen.</summary>
    public static string FileName(string prefix, string computer, DateTimeOffset date)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var name = new string($"{prefix} {computer} {date:yyyy-MM-dd}".Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return name + ".html";
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static void Section(StringBuilder html, string title) =>
        html.Append("<h2>").Append(E(title)).Append("</h2>\n");

    private static void Missing(StringBuilder html, Func<string, string> L) =>
        html.Append("<p class=\"muted\">").Append(E(L("NotAvailable"))).Append("</p>\n");

    private static void Table(StringBuilder html, IEnumerable<(string Key, string Value)> rows, string? badge = null)
    {
        html.Append("<table class=\"kv\"><tbody>");
        var first = true;
        foreach (var (key, value) in rows)
        {
            html.Append("<tr><th>").Append(E(key)).Append("</th><td>").Append(E(value));
            if (first && badge is not null)
            {
                html.Append(' ').Append(badge);
            }

            first = false;
            html.Append("</td></tr>");
        }

        html.Append("</tbody></table>\n");
    }

    private static void Row(StringBuilder html, string name, double? now, double? before, string format, string unit, CultureInfo culture)
    {
        if (now is not { } value)
        {
            return;
        }

        var change = Benchmarks.Change(now, before);
        html.Append("<tr><td>").Append(E(name)).Append("</td><td>").Append(E(value.ToString(format, culture) + unit)).Append("</td><td>");
        if (change is { } c)
        {
            var css = c >= 1 ? "good" : c <= -1 ? "bad" : "muted";
            html.Append("<span class=\"").Append(css).Append("\">").Append(E((c > 0 ? "+" : string.Empty) + c.ToString("0", culture) + " %")).Append("</span>");
        }
        else
        {
            html.Append("–");
        }

        html.Append("</td></tr>");
    }

    private static string Badge(HealthLevel level, Func<string, string> L) => level switch
    {
        HealthLevel.Good => $"<span class=\"badge good\">{E(L("Good"))}</span>",
        HealthLevel.Warning => $"<span class=\"badge warn\">{E(L("Warning"))}</span>",
        HealthLevel.Bad => $"<span class=\"badge bad\">{E(L("Bad"))}</span>",
        _ => $"<span class=\"badge\">{E(L("Unknown"))}</span>",
    };

    private const string Css = """
        :root{--bg:#f6f7f9;--card:#fff;--text:#16181d;--muted:#6b7280;--line:#e5e7eb;--blue:#1f5eff;--good:#16a34a;--warn:#d97706;--bad:#dc2626}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 "Segoe UI",system-ui,sans-serif}
        main{max-width:880px;margin:32px auto;padding:32px 40px;background:var(--card);border-radius:16px;box-shadow:0 2px 12px rgba(0,0,0,.06)}
        header{border-bottom:3px solid var(--blue);padding-bottom:16px;margin-bottom:8px}
        .brand{color:var(--blue);font-weight:700;letter-spacing:.04em;text-transform:uppercase;font-size:12px}
        h1{margin:4px 0;font-size:28px}
        h2{margin:28px 0 10px;font-size:18px;color:var(--blue)}
        h3{margin:14px 0 4px;font-size:14px;color:var(--muted);letter-spacing:.04em}
        table{width:100%;border-collapse:collapse}
        th,td{text-align:left;padding:7px 10px;border-bottom:1px solid var(--line);vertical-align:top}
        .kv th{width:34%;color:var(--muted);font-weight:500}
        .grid thead th{color:var(--muted);font-weight:600;font-size:13px}
        ul{margin:4px 0 0;padding-left:20px;columns:2;column-gap:32px}
        li{break-inside:avoid}
        .muted{color:var(--muted)}.small{font-size:12px}
        .good{color:var(--good)}.bad{color:var(--bad)}
        .badge{display:inline-block;padding:1px 9px;border-radius:999px;font-size:12px;font-weight:600;background:#eef0f3;color:var(--muted)}
        .badge.good{background:#dcfce7;color:var(--good)}.badge.warn{background:#fef3c7;color:var(--warn)}.badge.bad{background:#fee2e2;color:var(--bad)}
        footer{margin-top:32px;padding-top:12px;border-top:1px solid var(--line);color:var(--muted);font-size:12px}
        @media print{body{background:#fff}main{box-shadow:none;margin:0;max-width:none;padding:0}h2{break-after:avoid}tr{break-inside:avoid}}
        """;
}
