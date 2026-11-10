using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tools;
using MunzQuickTweaks.Core.Tweaks;
using Xunit;

namespace MunzQuickTweaks.Tests;

public sealed class GpuPreferencesTests
{
    private const string Game = @"C:\Games\spiel.exe";
    private readonly FakeRegistry _reg = new();

    [Fact]
    public void Set_keeps_other_parts_and_remove_restores_them()
    {
        _reg.Seed(RegistryHive.HKCU, GpuPreferences.Key, Game, RegistryKind.String, "SwapEffectUpgradeEnable=1;");
        var gpu = new GpuPreferences(_reg);

        gpu.Set(Game, GpuChoice.HighPerformance);
        Assert.Equal("GpuPreference=2;SwapEffectUpgradeEnable=1;", _reg.Read(RegistryHive.HKCU, GpuPreferences.Key, Game)!.Data);
        Assert.Equal(GpuChoice.HighPerformance, Assert.Single(gpu.List()).Choice);

        gpu.Remove(Game);
        Assert.Equal("SwapEffectUpgradeEnable=1;", _reg.Read(RegistryHive.HKCU, GpuPreferences.Key, Game)!.Data);
        Assert.Empty(gpu.List());
    }

    [Fact]
    public void Remove_deletes_value_when_nothing_else_is_left()
    {
        var gpu = new GpuPreferences(_reg);
        gpu.Set(Game, GpuChoice.PowerSaving);
        gpu.Remove(Game);
        Assert.Null(_reg.Read(RegistryHive.HKCU, GpuPreferences.Key, Game));
    }

    [Fact]
    public void Global_settings_value_is_not_listed()
    {
        _reg.Seed(RegistryHive.HKCU, GpuPreferences.Key, "DirectXUserGlobalSettings", RegistryKind.String, "GpuPreference=2;");
        Assert.Empty(new GpuPreferences(_reg).List());
    }

    [Theory]
    [InlineData("spiel.exe")]
    [InlineData(@"C:\x\..\spiel.exe")]
    [InlineData(@"C:\Games\spiel.bat")]
    public void Only_full_exe_paths_are_accepted(string path) =>
        Assert.Throws<ArgumentException>(() => new GpuPreferences(_reg).Set(path, GpuChoice.HighPerformance));
}

public sealed class NetworkPresetTests
{
    [Fact]
    public void Detect_recognises_providers_auto_and_custom()
    {
        Assert.Equal("auto", NetworkPresets.Detect(Array.Empty<string>()));
        Assert.Equal("cloudflare", NetworkPresets.Detect(new[] { "1.1.1.1", "1.0.0.1" }));
        Assert.Equal("quad9", NetworkPresets.Detect(new[] { "9.9.9.9", "2620:fe::fe" }));
        Assert.Null(NetworkPresets.Detect(new[] { "192.168.178.1" }));
    }

    [Fact]
    public void Every_provider_has_ipv4_and_ipv6()
    {
        Assert.All(NetworkPresets.Providers, p =>
        {
            Assert.Contains(p.Servers, s => s.Contains('.'));
            Assert.Contains(p.Servers, s => s.Contains(':'));
        });
    }
}

public sealed class HealthAssessmentTests
{
    private static DiskHealth Disk(int status = 0, int? wear = 5, int? temp = 40, long? errors = 0) =>
        new("SSD", "NVMe SSD", 1_000_000_000_000, status, wear, temp, 1000, errors);

    [Fact]
    public void Disk_levels()
    {
        Assert.Equal(HealthLevel.Good, HealthAssessment.Disk(Disk()));
        Assert.Equal(HealthLevel.Warning, HealthAssessment.Disk(Disk(wear: 85)));
        Assert.Equal(HealthLevel.Warning, HealthAssessment.Disk(Disk(temp: 75)));
        Assert.Equal(HealthLevel.Warning, HealthAssessment.Disk(Disk(status: 1)));
        Assert.Equal(HealthLevel.Bad, HealthAssessment.Disk(Disk(errors: 3)));
        Assert.Equal(HealthLevel.Bad, HealthAssessment.Disk(Disk(status: 2)));
        Assert.Equal(HealthLevel.Unknown, HealthAssessment.Disk(Disk(status: 5, wear: null, temp: null, errors: null)));
    }

    [Fact]
    public void Battery_percent_and_level()
    {
        Assert.Equal(90, HealthAssessment.BatteryPercent(new BatteryHealth(50000, 45000, 100)));
        Assert.Equal(100, HealthAssessment.BatteryPercent(new BatteryHealth(50000, 52000, null)));
        Assert.Equal(HealthLevel.Warning, HealthAssessment.Battery(new BatteryHealth(50000, 35000, null)));
        Assert.Equal(HealthLevel.Bad, HealthAssessment.Battery(new BatteryHealth(50000, 20000, null)));
        Assert.Equal(HealthLevel.Unknown, HealthAssessment.Battery(new BatteryHealth(0, 20000, null)));
    }

    [Fact]
    public void Boot_compare_splits_at_first_tweak()
    {
        var since = new DateTime(2026, 11, 5);
        var boots = new[]
        {
            new BootRecord(new DateTime(2026, 11, 1), 40000, 30000, 10000),
            new BootRecord(new DateTime(2026, 11, 6), 30000, 20000, 10000),
        };
        Assert.Equal((30.0, 20.0), BootTimes.Compare(boots, since));
        Assert.Equal((null, 25.0), BootTimes.Compare(boots, new DateTime(2026, 10, 1)));
    }
}

public sealed class InstallCatalogTests
{
    [Fact]
    public void Ids_are_unique_and_every_group_has_apps()
    {
        Assert.Equal(InstallCatalog.All.Count, InstallCatalog.All.Select(a => a.WingetId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Enum.GetValues<InstallGroup>(), g => Assert.Contains(InstallCatalog.All, a => a.Group == g));
        Assert.InRange(InstallCatalog.All.Count, 35, 60);
    }

    [Fact]
    public void Export_is_parsed()
    {
        const string json = """
        { "Sources": [ { "Packages": [ { "PackageIdentifier": "Valve.Steam" }, { "PackageIdentifier": "7zip.7zip" } ], "SourceDetails": {} } ] }
        """;
        var ids = InstallCatalog.ParseExport(json);
        Assert.Contains("valve.steam", ids);
        Assert.Equal(2, ids.Count);
        Assert.Empty(InstallCatalog.ParseExport("kaputt"));
    }

    [Fact]
    public void Exit_codes_are_interpreted()
    {
        Assert.True(InstallCatalog.Interpret(0, string.Empty).Success);
        var already = InstallCatalog.Interpret(unchecked((int)0x8A15002B), string.Empty);
        Assert.True(already.Success && already.AlreadyInstalled);
        var failed = InstallCatalog.Interpret(unchecked((int)0x8A150014), "Suche...\nKein Paket gefunden\n");
        Assert.False(failed.Success);
        Assert.Contains("Kein Paket gefunden", failed.Error);
    }
}

public sealed class ResetNoticeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-notice-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Notifies_once_per_set_and_again_after_change()
    {
        var notice = new MunzQuickTweaks.Core.Safety.ResetNotice(Path.Combine(_dir, "n.json"));
        Assert.False(notice.ShouldNotify(Array.Empty<string>()));
        Assert.True(notice.ShouldNotify(new[] { "b", "a" }));
        notice.Remember(new[] { "a", "b" });
        Assert.False(notice.ShouldNotify(new[] { "b", "a" }));
        Assert.True(notice.ShouldNotify(new[] { "a", "b", "c" }));

        // Nach erneutem Anwenden ist die Liste leer, dann soll derselbe Satz wieder gemeldet werden
        notice.Remember(Array.Empty<string>());
        Assert.True(notice.ShouldNotify(new[] { "a", "b" }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
