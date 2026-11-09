using System.Text.Json;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tweaks;
using Xunit;

namespace MunzQuickTweaks.Tests;

internal sealed class FakeAppEnvironment : IAppEnvironment
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.ContainsKey(path) || Existing.Contains(path);

    public string? ReadText(string path) => Files.GetValueOrDefault(path);

    public void WriteText(string path, string text) => Files[path] = text;

    public bool IsProcessRunning(string processName) => Running.Contains(processName);
}

public sealed class JsonSettingTests : IDisposable
{
    private const string File = @"%APPDATA%\discord\settings.json";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-json-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAppEnvironment _env = new();

    private TweakEngine Engine() => new(new FakeRegistry(), new JournalStore(Path.Combine(_dir, "j.json")), new FakeNotifier(), 26100, apps: _env);

    private static TweakDefinition HwAccel() => new()
    {
        Id = "apps.discord.hwaccel.off",
        Category = TweakCategory.Apps,
        App = "discord",
        Title = new() { De = "x", En = "x" },
        Description = new() { De = "x", En = "x" },
        Actions =
        {
            new JsonSettingAction
            {
                File = File,
                Property = "enableHardwareAcceleration",
                Value = JsonSerializer.SerializeToElement(false),
                Default = JsonSerializer.SerializeToElement(true),
                Process = "Discord",
            },
        },
    };

    [Fact]
    public void Missing_settings_file_makes_tweak_unavailable()
    {
        Assert.Equal(TweakStatus.NotAvailable, Engine().GetStatus(HwAccel()));
    }

    [Fact]
    public void Apply_and_revert_restore_the_exact_file_values()
    {
        _env.Files[File] = """{ "IS_MAXIMIZED": false, "enableHardwareAcceleration": true }""";
        var engine = Engine();

        Assert.Equal(TweakStatus.NotApplied, engine.GetStatus(HwAccel()));
        Assert.True(engine.Apply(HwAccel()).Success);
        Assert.Equal(TweakStatus.Applied, engine.GetStatus(HwAccel()));
        Assert.Contains("\"enableHardwareAcceleration\": false", _env.Files[File]);
        Assert.Contains("IS_MAXIMIZED", _env.Files[File]);

        Assert.True(engine.Revert(HwAccel()).Success);
        Assert.Equal(TweakStatus.NotApplied, engine.GetStatus(HwAccel()));
        Assert.Contains("\"enableHardwareAcceleration\": true", _env.Files[File]);
    }

    [Fact]
    public void Revert_removes_a_setting_that_did_not_exist_before()
    {
        _env.Files[File] = """{ "IS_MAXIMIZED": false }""";
        var engine = Engine();

        Assert.True(engine.Apply(HwAccel()).Success);
        Assert.True(engine.Revert(HwAccel()).Success);
        Assert.DoesNotContain("enableHardwareAcceleration", _env.Files[File]);
    }

    [Fact]
    public void Running_program_blocks_the_change_and_leaves_file_untouched()
    {
        const string original = """{ "enableHardwareAcceleration": true }""";
        _env.Files[File] = original;
        _env.Running.Add("Discord");

        var result = Engine().Apply(HwAccel());
        Assert.False(result.Success);
        Assert.Contains("Discord", result.Error);
        Assert.Equal(original, _env.Files[File]);
    }

    [Fact]
    public void Broken_settings_file_fails_cleanly()
    {
        const string broken = "{ kaputt";
        _env.Files[File] = broken;

        var result = Engine().Apply(HwAccel());
        Assert.False(result.Success);
        Assert.Equal(broken, _env.Files[File]);
    }

    [Fact]
    public void Json_tweak_counts_as_user_setting_on_foreign_account()
    {
        _env.Files[File] = "{}";
        var engine = Engine();
        engine.UserHiveAllowed = false;
        Assert.True(engine.IsBlockedByAccount(HwAccel()));
        Assert.Equal(TweakStatus.NotAvailable, engine.GetStatus(HwAccel()));
    }

    [Fact]
    public void Requires_hides_tweak_until_program_is_installed()
    {
        var tweak = new TweakDefinition
        {
            Id = "apps.edge.test",
            Category = TweakCategory.Apps,
            App = "edge",
            Requires = { @"%ProgramFiles%\Edge\msedge.exe" },
            Actions = { new RegistryAction { Hive = RegistryHive.HKLM, Key = @"SOFTWARE\Policies\Test", Name = "A", Kind = RegistryKind.DWord, Value = "0" } },
        };
        var engine = Engine();
        Assert.Equal(TweakStatus.NotAvailable, engine.GetStatus(tweak));
        Assert.Equal("Programm nicht installiert", engine.UnavailableReason(tweak));

        _env.Existing.Add(@"%ProgramFiles%\Edge\msedge.exe");
        Assert.Equal(TweakStatus.NotApplied, engine.GetStatus(tweak));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class CatalogEtappe2Tests
{
    private static Stream Json(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Json_setting_outside_user_folders_is_rejected()
    {
        const string bad = """
        [{ "id": "apps.x.y", "category": "Apps", "app": "x", "title": {"de":"a","en":"a"}, "description": {"de":"a","en":"a"},
           "actions": [{ "type": "jsonSetting", "file": "C:\\Windows\\x.json", "property": "a", "value": true }] }]
        """;
        Assert.Throws<CatalogException>(() => CatalogLoader.Load(new[] { ("t.json", Json(bad)) }));
    }

    [Fact]
    public void App_tweak_without_app_id_is_rejected()
    {
        const string bad = """
        [{ "id": "apps.x.y", "category": "Apps", "title": {"de":"a","en":"a"}, "description": {"de":"a","en":"a"},
           "actions": [{ "type": "registry", "hive": "HKLM", "key": "SOFTWARE\\X", "name": "a", "kind": "DWord", "value": "1" }] }]
        """;
        Assert.Throws<CatalogException>(() => CatalogLoader.Load(new[] { ("t.json", Json(bad)) }));
    }

    [Fact]
    public void Every_app_tweak_belongs_to_a_known_app()
    {
        var known = AppCatalog.All.Select(a => a.Id).ToHashSet();
        var apps = ProfileTests.RepoCatalog().Where(t => t.Category == TweakCategory.Apps).ToList();
        Assert.NotEmpty(apps);
        Assert.All(apps, t => Assert.Contains(t.App!, known));
        Assert.All(apps, t => Assert.NotEmpty(t.Requires));
    }

    [Fact]
    public void Details_describe_json_settings()
    {
        var tweak = ProfileTests.RepoCatalog().Single(t => t.Id == "apps.discord.hwaccel.off");
        var lines = TweakDetails.Describe(tweak, "de");
        Assert.Contains(lines, l => l.Contains("enableHardwareAcceleration") && l.Contains("false"));
        Assert.Contains(lines, l => l.Contains("Discord muss dafür geschlossen sein"));
    }
}

public sealed class ProcessRulesTests
{
    private readonly FakeRegistry _reg = new();

    [Theory]
    [InlineData("spiel.exe", true)]
    [InlineData("Cyberpunk2077.exe", true)]
    [InlineData("C:\\x\\spiel.exe", false)]
    [InlineData("spiel", false)]
    [InlineData("csrss.exe", false)]
    [InlineData("svchost.exe", false)]
    [InlineData("..\\x.exe", false)]
    public void Validate_accepts_only_plain_non_system_exe_names(string exe, bool ok) =>
        Assert.Equal(ok, ProcessRules.Validate(exe) is null);

    [Fact]
    public void Set_list_and_remove_leave_no_traces()
    {
        var rules = new ProcessRules(_reg);
        rules.Set(new ProcessRule("spiel.exe", CpuPriority.High, IoPriority.Low));

        var rule = Assert.Single(rules.List());
        Assert.Equal(CpuPriority.High, rule.Cpu);
        Assert.Equal(IoPriority.Low, rule.Io);

        rules.Remove("spiel.exe");
        Assert.Empty(rules.List());
        Assert.False(_reg.KeyExists(RegistryHive.HKLM, $@"{ProcessRules.BaseKey}\spiel.exe\PerfOptions"));
    }

    [Fact]
    public void Remove_keeps_foreign_values_like_debugger()
    {
        var exeKey = $@"{ProcessRules.BaseKey}\tool.exe";
        _reg.Seed(RegistryHive.HKLM, exeKey, "Debugger", RegistryKind.String, "x.exe");
        var rules = new ProcessRules(_reg);
        rules.Set(new ProcessRule("tool.exe", CpuPriority.BelowNormal, null));
        rules.Remove("tool.exe");

        Assert.Equal("x.exe", _reg.Read(RegistryHive.HKLM, exeKey, "Debugger")!.Data);
    }

    [Fact]
    public void Entries_without_perf_options_are_not_listed()
    {
        _reg.Seed(RegistryHive.HKLM, $@"{ProcessRules.BaseKey}\other.exe", "Debugger", RegistryKind.String, "x");
        Assert.Empty(new ProcessRules(_reg).List());
    }

    [Fact]
    public void Realtime_cannot_be_expressed()
    {
        Assert.False(Enum.IsDefined(typeof(CpuPriority), 4));
    }
}

public sealed class StartupEtappe2Tests
{
    [Fact]
    public void Missing_only_for_full_paths_that_do_not_exist()
    {
        Assert.True(StartupApproved.IsMissing("\"C:\\Weg\\alt.exe\" --tray", _ => false));
        Assert.False(StartupApproved.IsMissing("\"C:\\Da\\neu.exe\"", _ => true));
        Assert.False(StartupApproved.IsMissing("rundll32.exe shell32.dll,Control_RunDLL", _ => false));
    }

    [Fact]
    public void Reg_file_escapes_quotes_and_backslashes()
    {
        var text = StartupApproved.RegFile("HKEY_CURRENT_USER", @"Software\Run", "App", "\"C:\\A\\a.exe\" -x");
        Assert.StartsWith("Windows Registry Editor Version 5.00", text);
        Assert.Contains(@"[HKEY_CURRENT_USER\Software\Run]", text);
        Assert.Contains(@"""App""=""\""C:\\A\\a.exe\"" -x""", text);
    }

    [Fact]
    public void Can_remove_only_broken_non_task_entries()
    {
        var entry = new StartupEntry("Run|x", "x", "C:\\x.exe", StartupSource.UserRegistry, true) { FileMissing = true };
        Assert.True(entry.CanRemove);
        Assert.False((entry with { FileMissing = false }).CanRemove);
        Assert.False((entry with { Source = StartupSource.ScheduledTask }).CanRemove);
    }

    [Fact]
    public void App_startup_matching_ignores_scheduled_tasks()
    {
        var discord = AppCatalog.All.Single(a => a.Id == "discord");
        var entries = new[]
        {
            new StartupEntry("Run|Discord", "Discord", @"C:\Users\a\AppData\Local\Discord\Update.exe --processStart Discord.exe", StartupSource.UserRegistry, true),
            new StartupEntry(@"\DiscordTask", "Task", @"C:\Discord\Discord.exe", StartupSource.ScheduledTask, true),
            new StartupEntry("Run|Steam", "Steam", @"C:\Steam\steam.exe -silent", StartupSource.UserRegistry, true),
        };

        var match = Assert.Single(AppCatalog.MatchStartup(discord, entries));
        Assert.Equal("Discord", match.Name);
    }
}

public sealed class PowerModeTests
{
    [Theory]
    [InlineData(PowerMode.Efficiency)]
    [InlineData(PowerMode.Balanced)]
    [InlineData(PowerMode.Performance)]
    public void Mode_ids_round_trip(PowerMode mode) =>
        Assert.Equal(mode, PowerModeIds.FromGuid(PowerModeIds.ToGuid(mode)));

    [Fact]
    public void Unknown_overlay_is_null() => Assert.Null(PowerModeIds.FromGuid(Guid.NewGuid()));
}
