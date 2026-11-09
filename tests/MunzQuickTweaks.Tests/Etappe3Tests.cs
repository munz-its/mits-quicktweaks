using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tools;
using MunzQuickTweaks.Core.Tweaks;
using Xunit;

namespace MunzQuickTweaks.Tests;

public sealed class DiskAnalyzerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-disk-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Scan_sums_subfolders_and_sorts_largest_first()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "klein"));
        Directory.CreateDirectory(Path.Combine(_dir, "gross", "tief"));
        File.WriteAllBytes(Path.Combine(_dir, "klein", "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_dir, "gross", "b.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_dir, "gross", "tief", "c.bin"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(_dir, "datei.bin"), new byte[500]);

        var items = DiskAnalyzer.Scan(_dir);

        Assert.Equal(new[] { "gross", "datei.bin", "klein" }, items.Select(i => i.Name));
        Assert.Equal(3000, items[0].Size);
        Assert.Equal(2, items[0].Files);
        Assert.False(items[1].IsDirectory);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    [InlineData(300L * 1024 * 1024, "300 MB")]
    public void FormatSize_is_readable(long bytes, string expected) =>
        Assert.Equal(expected, DiskAnalyzer.FormatSize(bytes, System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class DriverAdvisorTests
{
    [Theory]
    [InlineData("NVIDIA", "nvidia.com")]
    [InlineData("Advanced Micro Devices, Inc.", "amd.com")]
    [InlineData("Intel Corporation", "intel")]
    [InlineData("Realtek Semiconductor Corp.", "realtek.com")]
    [InlineData("Qualcomm", "ms-settings:")]
    public void Links_point_to_official_vendor_pages(string manufacturer, string expected) =>
        Assert.Contains(expected, DriverAdvisor.LinkFor(manufacturer));

    [Fact]
    public void Old_means_older_than_two_years()
    {
        var today = new DateTime(2026, 11, 10);
        Assert.True(DriverAdvisor.IsOld(new DriverInfo("x", "NET", "Intel", "1", new DateTime(2024, 1, 1), true), today));
        Assert.False(DriverAdvisor.IsOld(new DriverInfo("x", "NET", "Intel", "1", new DateTime(2025, 6, 1), true), today));
        Assert.False(DriverAdvisor.IsOld(new DriverInfo("x", "NET", "Intel", "1", null, true), today));
    }

    [Fact]
    public void Relevant_drops_unimportant_classes_microsoft_generics_and_duplicates()
    {
        var list = DriverAdvisor.Relevant(new[]
        {
            new DriverInfo("GeForce RTX", "DISPLAY", "NVIDIA", "32.0", null, true),
            new DriverInfo("GeForce RTX", "DISPLAY", "NVIDIA", "32.0", null, true),
            new DriverInfo("Microsoft Basic Display", "DISPLAY", "Microsoft", "10.0", null, true),
            new DriverInfo("WAN Miniport", "NET", "Microsoft", "10.0", null, true),
            new DriverInfo("Realtek Audio", "MEDIA", "Realtek", "6.0", null, true),
            new DriverInfo("HID Keyboard", "KEYBOARD", "(Standard keyboards)", "10.0", null, true),
        });

        Assert.Equal(new[] { "GeForce RTX", "Microsoft Basic Display", "Realtek Audio" }, list.Select(d => d.Device));
    }
}

public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("1.0.0", "0.9.1", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("0.9.0", "1.0.0", false)]
    public void IsNewer_compares_versions(string offered, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(new UpdateInfo { Version = offered }, current));

    [Fact]
    public void Parse_accepts_github_link()
    {
        var info = UpdateCheck.Parse("""{ "version": "1.0.0", "url": "https://github.com/x/mits-quicktweaks/releases/latest", "notesDe": "Neu" }""");
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal("Neu", info.NotesDe);
    }

    [Theory]
    [InlineData("""{ "version": "1.0.0", "url": "http://github.com/x" }""")]
    [InlineData("""{ "version": "1.0.0", "url": "https://evil.example/x.exe" }""")]
    [InlineData("""{ "version": "eins", "url": "https://github.com/x" }""")]
    public void Parse_rejects_unsafe_or_broken_files(string json) =>
        Assert.Throws<FormatException>(() => UpdateCheck.Parse(json));
}

public sealed class BenchmarkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-bench-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Cpu_and_ram_return_positive_rates()
    {
        Assert.True(Benchmarks.CpuSingle(TimeSpan.FromMilliseconds(200)) > 0);
        Assert.True(Benchmarks.CpuMulti(TimeSpan.FromMilliseconds(200)) > 0);
        Assert.True(Benchmarks.RamCopy(TimeSpan.FromMilliseconds(200)) > 0);
    }

    [Fact]
    public void Disk_test_measures_and_removes_its_file()
    {
        Directory.CreateDirectory(_dir);
        var (write, read, iops) = Benchmarks.Disk(_dir, 16L * 1024 * 1024, TimeSpan.FromMilliseconds(200));
        Assert.True(write > 0 && read > 0 && iops > 0);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Change_in_percent()
    {
        Assert.Equal(10, Benchmarks.Change(110, 100));
        Assert.Equal(-50, Benchmarks.Change(50, 100));
        Assert.Null(Benchmarks.Change(50, null));
    }

    [Fact]
    public void Store_returns_newest_first_and_skips_broken_lines()
    {
        var path = Path.Combine(_dir, "b.jsonl");
        var store = new BenchmarkStore(path);
        store.Add(new BenchmarkResult { CpuMulti = 1 });
        File.AppendAllText(path, "kaputt\n");
        store.Add(new BenchmarkResult { CpuMulti = 2 });

        Assert.Equal(new double?[] { 2, 1 }, store.All().Select(r => r.CpuMulti));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

internal sealed class FakeGameModeSystem : IGameModeSystem
{
    public bool IsModernStandby { get; set; }

    public Guid Active { get; set; } = TweakEngine.BalancedScheme;

    public HashSet<Guid> Plans { get; } = new() { TweakEngine.BalancedScheme, GameModeController.HighPerformance };

    public Guid? Overlay { get; set; } = Guid.Empty;

    public HashSet<string> Running { get; } = new() { "WSearch", "DoSvc" };

    public bool FailStop { get; set; }

    public Guid GetActivePlan() => Active;

    public bool PlanExists(Guid plan) => Plans.Contains(plan);

    public void SetActivePlan(Guid plan) => Active = plan;

    public Guid? GetOverlay() => Overlay;

    public void SetOverlay(Guid overlay) => Overlay = overlay;

    public bool IsServiceRunning(string service) => Running.Contains(service);

    public void StopService(string service)
    {
        if (FailStop)
        {
            throw new InvalidOperationException("geht nicht");
        }

        Running.Remove(service);
    }

    public void StartService(string service) => Running.Add(service);
}

public sealed class GameModeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-gm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGameModeSystem _system = new();
    private readonly FakeRegistry _reg = new();

    private GameModeController Controller() => new(_system, _reg, Path.Combine(_dir, "gm.json"));

    [Fact]
    public void On_and_off_restore_everything()
    {
        _reg.Seed(RegistryHive.HKLM, GameModeController.UpdateKey, "PauseUpdatesExpiryTime", RegistryKind.String, "2020-01-01T00:00:00Z");
        var controller = Controller();

        Assert.All(controller.TurnOn(), s => Assert.True(s.Success));
        Assert.True(controller.IsActive);
        Assert.Equal(GameModeController.HighPerformance, _system.Active);
        Assert.Empty(_system.Running);
        Assert.NotEqual("2020-01-01T00:00:00Z", _reg.Read(RegistryHive.HKLM, GameModeController.UpdateKey, "PauseUpdatesExpiryTime")!.Data);

        // Neue Instanz: Zustand kommt von der Platte, wie nach einem Neustart
        Assert.All(Controller().TurnOff(), s => Assert.True(s.Success));
        Assert.False(Controller().IsActive);
        Assert.Equal(TweakEngine.BalancedScheme, _system.Active);
        Assert.Contains("WSearch", _system.Running);
        Assert.Equal("2020-01-01T00:00:00Z", _reg.Read(RegistryHive.HKLM, GameModeController.UpdateKey, "PauseUpdatesExpiryTime")!.Data);
        Assert.Null(_reg.Read(RegistryHive.HKLM, GameModeController.UpdateKey, "PauseQualityUpdatesEndTime"));
    }

    [Fact]
    public void Modern_standby_uses_power_mode_instead_of_plan()
    {
        _system.IsModernStandby = true;
        var controller = Controller();
        controller.TurnOn();
        Assert.Equal(PowerModeIds.Performance, _system.Overlay);
        Assert.Equal(TweakEngine.BalancedScheme, _system.Active);

        controller.TurnOff();
        Assert.Equal(Guid.Empty, _system.Overlay);
    }

    [Fact]
    public void Services_that_were_not_running_stay_stopped()
    {
        _system.Running.Remove("DoSvc");
        var controller = Controller();
        controller.TurnOn();
        controller.TurnOff();
        Assert.DoesNotContain("DoSvc", _system.Running);
    }

    [Fact]
    public void Failed_step_is_reported_but_others_continue()
    {
        _system.FailStop = true;
        var steps = Controller().TurnOn();
        Assert.Contains(steps, s => !s.Success && s.Name.StartsWith("Service.", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Success && s.Name == "Update");
        Assert.Equal(GameModeController.HighPerformance, _system.Active);
    }

    [Fact]
    public void Turning_on_twice_does_not_overwrite_the_saved_state()
    {
        var controller = Controller();
        controller.TurnOn();
        Assert.Empty(controller.TurnOn());
        controller.TurnOff();
        Assert.Equal(TweakEngine.BalancedScheme, _system.Active);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
