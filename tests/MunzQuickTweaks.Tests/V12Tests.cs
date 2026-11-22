using MunzQuickTweaks.Core.Tools;
using Xunit;

namespace MunzQuickTweaks.Tests;

public class UpgradeParserTests
{
    private const string German =
        "   - \r   \\ \r   | \r" +
        "Name                       ID                          Version      Verfügbar    Quelle\n" +
        "------------------------------------------------------------------------------------------\n" +
        "Mozilla Firefox (x64 de)   Mozilla.Firefox             131.0        132.0.1      winget\n" +
        "7-Zip 23.01 (x64)          7zip.7zip                   23.01        24.08        winget\n" +
        "App Installer              Microsoft.AppInstaller      1.23.1911.0  1.24.25200.0 winget\n" +
        "Microsoft Visual C++ 2015… Microsoft.VCRedist.2015+.x… 14.36.32532  14.40.33810  winget\n" +
        "4 Aktualisierungen verfügbar.\n" +
        "\n" +
        "1 Paket(e) haben Versionsnummern, die nicht ermittelt werden können.\n";

    [Fact]
    public void Reads_german_table_and_skips_footer()
    {
        var known = new[] { "Mozilla.Firefox", "7zip.7zip", "Microsoft.VCRedist.2015+.x64" };
        var list = UpgradeParser.Parse(German, known);

        Assert.Equal(3, list.Count);
        Assert.Equal(new UpgradablePackage("Mozilla Firefox (x64 de)", "Mozilla.Firefox", "131.0", "132.0.1"), list[0]);
        Assert.Equal("24.08", list[1].Available);
        Assert.Equal("Microsoft.VCRedist.2015+.x64", list[2].Id);
    }

    [Fact]
    public void Never_updates_winget_itself()
    {
        Assert.DoesNotContain(UpgradeParser.Parse(German), p => p.Id == "Microsoft.AppInstaller");
    }

    [Fact]
    public void Drops_truncated_ids_that_are_not_unique()
    {
        var list = UpgradeParser.Parse(German, new[] { "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86" });
        Assert.DoesNotContain(list, p => p.Id.StartsWith("Microsoft.VCRedist", StringComparison.Ordinal));

        var withoutList = UpgradeParser.Parse(German);
        Assert.DoesNotContain(withoutList, p => p.Id.Contains('…'));
    }

    [Fact]
    public void Reads_english_table()
    {
        const string english =
            "Name            Id                 Version  Available Source\r\n" +
            "-------------------------------------------------------------\r\n" +
            "Discord         Discord.Discord    1.0.9163 1.0.9170  winget\r\n" +
            "1 upgrades available.\r\n";
        var list = UpgradeParser.Parse(english);
        Assert.Single(list);
        Assert.Equal("Discord.Discord", list[0].Id);
        Assert.Equal("1.0.9170", list[0].Available);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Es wurde kein installiertes Paket gefunden, das den Eingabekriterien entspricht.")]
    [InlineData("No installed package found matching input criteria.\n")]
    public void Empty_or_message_only_gives_nothing(string output)
    {
        Assert.Empty(UpgradeParser.Parse(output));
    }
}

public class ProfileFileTests
{
    [Fact]
    public void Round_trip_keeps_everything()
    {
        var app = MunzQuickTweaks.Core.Modules.DebloatCatalog.All[0].Package;
        var file = new MunzQuickTweaks.Core.Modules.ProfileFile
        {
            Name = "Kunden-PC",
            CreatedWith = "1.2.0",
            Tweaks = new() { "ui.fileext.show", "privacy.adid.off" },
            PowerMode = MunzQuickTweaks.Core.Modules.PowerMode.Performance,
            RemoveApps = new() { app },
            DisableStartup = new() { "steam" },
        };

        var json = file.Serialize();
        Assert.Contains("\"powerMode\": \"Performance\"", json, StringComparison.Ordinal);

        var (back, error) = MunzQuickTweaks.Core.Modules.ProfileFile.Parse(json);
        Assert.Equal(MunzQuickTweaks.Core.Modules.ProfileFileError.None, error);
        Assert.NotNull(back);
        Assert.Equal("Kunden-PC", back!.Name);
        Assert.Equal(file.Tweaks, back.Tweaks);
        Assert.Equal(MunzQuickTweaks.Core.Modules.PowerMode.Performance, back.PowerMode);

        var (profile, unknown) = back.ToProfile(new[] { "ui.fileext.show", "privacy.adid.off" });
        Assert.Equal(0, unknown);
        Assert.Equal(2, profile.TweakIds.Count);
        Assert.Equal(new[] { app }, profile.DebloatApps);
        Assert.Equal(new[] { "steam" }, profile.StartupApps);
    }

    [Fact]
    public void Unknown_entries_are_counted_and_dropped()
    {
        var file = new MunzQuickTweaks.Core.Modules.ProfileFile
        {
            Tweaks = new() { "ui.fileext.show", "future.tweak" },
            RemoveApps = new() { "Some.Unknown.App" },
            DisableStartup = new() { "nope" },
        };
        var (profile, unknown) = file.ToProfile(new[] { "ui.fileext.show" });
        Assert.Equal(3, unknown);
        Assert.Single(profile.TweakIds);
        Assert.Empty(profile.DebloatApps);
        Assert.Empty(profile.StartupApps);
    }

    [Theory]
    [InlineData("kein json")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"etwas-anderes\",\"formatVersion\":1}")]
    [InlineData("[1,2,3]")]
    public void Rejects_invalid_files(string json)
    {
        var (profile, error) = MunzQuickTweaks.Core.Modules.ProfileFile.Parse(json);
        Assert.Null(profile);
        Assert.Equal(MunzQuickTweaks.Core.Modules.ProfileFileError.Invalid, error);
    }

    [Fact]
    public void Rejects_newer_format()
    {
        var (_, error) = MunzQuickTweaks.Core.Modules.ProfileFile.Parse("{\"format\":\"munz-quicktweaks-profile\",\"formatVersion\":99}");
        Assert.Equal(MunzQuickTweaks.Core.Modules.ProfileFileError.TooNew, error);
    }

    [Fact]
    public void Cleans_duplicates_and_blanks()
    {
        var (profile, _) = MunzQuickTweaks.Core.Modules.ProfileFile.Parse(
            "{\"format\":\"munz-quicktweaks-profile\",\"formatVersion\":1,\"tweaks\":[\"a\",\"A\",\" \",\"b\"],\"powerMode\":null}");
        Assert.Equal(new[] { "a", "b" }, profile!.Tweaks);
    }

    [Theory]
    [InlineData("Mein PC", "Mein PC.mqtprofile")]
    [InlineData("a/b:c*?", "abc.mqtprofile")]
    [InlineData("...", "Profil.mqtprofile")]
    public void Safe_file_names(string name, string expected)
    {
        Assert.Equal(expected, MunzQuickTweaks.Core.Modules.ProfileFile.SafeFileName(name));
    }
}

public class PcReportTests
{
    private static PcReportData Sample() => new()
    {
        ComputerName = "TEST-PC",
        Created = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero),
        AppVersion = "1.2.0",
        System = new MunzQuickTweaks.Core.Machine.SystemInfo("Windows 11 Pro", 26100, true, "AMD Ryzen 7 <script>", new[] { "NVIDIA RTX 4060" }, 32, true),
        Disks = new[] { new DiskHealth("Samsung 990 Pro", "SSD", 1_000_000_000_000, 0, 3, 41, 1234, 0) },
        Battery = new BatteryHealth(90000, 81000, 120),
        Drivers = new[] { new DriverInfo("Realtek Audio", "MEDIA", "Realtek", "6.0.9", new DateTime(2020, 1, 1), true) },
        Tweaks = new[] { ("Datenschutz", "Werbe-ID aus"), ("Leistung", "Startverzögerung aus") },
        Benchmark = new BenchmarkResult { Time = DateTimeOffset.Now, CpuSingle = 110, GpuFps = 500 },
        PreviousBenchmark = new BenchmarkResult { CpuSingle = 100 },
        BootSeconds = 12.3,
    };

    [Fact]
    public void Contains_all_sections_and_escapes_html()
    {
        var html = PcReport.Build(Sample(), key => key, System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("TEST-PC", html, StringComparison.Ordinal);
        Assert.Contains("AMD Ryzen 7 &lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Samsung 990 Pro", html, StringComparison.Ordinal);
        Assert.Contains("90 %", html, StringComparison.Ordinal); // Akku 81000 von 90000
        Assert.Contains("+10 %", html, StringComparison.Ordinal); // CPU 110 gegen 100
        Assert.Contains("Werbe-ID aus", html, StringComparison.Ordinal);
        Assert.Contains("Report.Old", html, StringComparison.Ordinal); // Treiber von 2020
        Assert.Contains("12,3 s", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Works_with_missing_data()
    {
        var html = PcReport.Build(new PcReportData { ComputerName = "X", Created = DateTimeOffset.Now, AppVersion = "1" }, key => key,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Report.NotAvailable", html, StringComparison.Ordinal);
        Assert.Contains("Report.NoBenchmark", html, StringComparison.Ordinal);
        Assert.Contains("Report.NoTweaks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Report.Battery\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void File_name_is_safe()
    {
        Assert.Equal("PC-Bericht DESK1 2026-11-22.html",
            PcReport.FileName("PC-Bericht", "DESK:1", new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero)));
    }
}
