using System.Text;
using MunzQuickTweaks.Core.Tweaks;
using Xunit;

namespace MunzQuickTweaks.Tests;

/// <summary>Registry im Speicher, verhält sich wie die echte (Schlüssel ohne Groß-/Kleinschreibung).</summary>
internal sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, RegistryValue>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public int Writes { get; private set; }

    /// <summary>Lässt das Schreiben dieses Werts scheitern (für Rollback-Tests).</summary>
    public string? FailOnName { get; set; }

    /// <summary>Schreibt einen anderen Wert als verlangt (für Kontroll-Tests).</summary>
    public string? IgnoreWritesTo { get; set; }

    private static string Path(RegistryHive hive, string key) => $"{hive}\\{key}";

    public void Seed(RegistryHive hive, string key, string name, RegistryKind kind, string data) =>
        Values(hive, key, create: true)![name] = new RegistryValue(kind, data);

    public void SeedKey(RegistryHive hive, string key) => Values(hive, key, create: true);

    public bool KeyExists(RegistryHive hive, string key) => _keys.ContainsKey(Path(hive, key));

    public RegistryValue? Read(RegistryHive hive, string key, string name) =>
        Values(hive, key, create: false)?.GetValueOrDefault(name);

    public void Write(RegistryHive hive, string key, string name, RegistryKind kind, string data)
    {
        if (name == FailOnName)
        {
            throw new UnauthorizedAccessException("Zugriff verweigert (Test)");
        }

        Writes++;
        if (name == IgnoreWritesTo)
        {
            return;
        }

        Values(hive, key, create: true)![name] = new RegistryValue(kind, data);
    }

    public void Delete(RegistryHive hive, string key, string name) => Values(hive, key, create: false)?.Remove(name);

    public void DeleteKeyIfEmpty(RegistryHive hive, string key)
    {
        if (Values(hive, key, create: false) is { Count: 0 })
        {
            _keys.Remove(Path(hive, key));
        }
    }

    public IReadOnlyList<string> SubKeys(RegistryHive hive, string key)
    {
        var prefix = Path(hive, key) + "\\";
        return _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, RegistryValue>? Values(RegistryHive hive, string key, bool create)
    {
        var path = Path(hive, key);
        if (!_keys.TryGetValue(path, out var values) && create)
        {
            values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
            _keys[path] = values;
        }

        return values;
    }
}

internal sealed class FakeNotifier : ISystemNotifier
{
    public int Refreshes { get; private set; }

    public void RefreshExplorer() => Refreshes++;
}

public sealed class EngineTests : IDisposable
{
    private const string AdvKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string NewKey = @"Software\MunzTest\Neu";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-engine-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRegistry _reg = new();
    private readonly FakeNotifier _notifier = new();

    private string JournalPath => Path.Combine(_dir, "journal.json");

    private TweakEngine CreateEngine(int build = 26100) =>
        new(_reg, new JournalStore(JournalPath), _notifier, build);

    private static TweakDefinition FileExt() => new()
    {
        Id = "ui.fileext.show",
        Restart = RestartHint.Explorer,
        Title = new() { De = "x", En = "x" },
        Description = new() { De = "x", En = "x" },
        Actions =
        {
            new RegistryAction { Hive = RegistryHive.HKCU, Key = AdvKey, Name = "HideFileExt", Kind = RegistryKind.DWord, Value = "0", Default = "1" },
        },
    };

    private static TweakDefinition TwoValues() => new()
    {
        Id = "test.two",
        Title = new() { De = "x", En = "x" },
        Description = new() { De = "x", En = "x" },
        Actions =
        {
            new RegistryAction { Hive = RegistryHive.HKCU, Key = NewKey, Name = "A", Kind = RegistryKind.DWord, Value = "1" },
            new RegistryAction { Hive = RegistryHive.HKCU, Key = NewKey, Name = "B", Kind = RegistryKind.String, Value = "an" },
        },
    };

    [Fact]
    public void Apply_sets_value_and_status_becomes_applied()
    {
        _reg.Seed(RegistryHive.HKCU, AdvKey, "HideFileExt", RegistryKind.DWord, "1");
        var engine = CreateEngine();

        Assert.Equal(TweakStatus.NotApplied, engine.GetStatus(FileExt()));
        Assert.True(engine.Apply(FileExt()).Success);
        Assert.Equal("0", _reg.Read(RegistryHive.HKCU, AdvKey, "HideFileExt")!.Data);
        Assert.Equal(TweakStatus.Applied, engine.GetStatus(FileExt()));
        Assert.Equal(1, _notifier.Refreshes);
    }

    [Fact]
    public void Revert_restores_exact_previous_value_from_journal()
    {
        // Ungewöhnlicher Ausgangswert: Rückgängig muss genau diesen herstellen, nicht den Standard
        _reg.Seed(RegistryHive.HKCU, AdvKey, "HideFileExt", RegistryKind.DWord, "7");
        var engine = CreateEngine();

        engine.Apply(FileExt());
        Assert.True(engine.Revert(FileExt()).Success);

        Assert.Equal("7", _reg.Read(RegistryHive.HKCU, AdvKey, "HideFileExt")!.Data);
        Assert.Null(engine.Journal.Get("ui.fileext.show"));
    }

    [Fact]
    public void Revert_deletes_values_and_keys_the_tweak_created()
    {
        var engine = CreateEngine();

        engine.Apply(TwoValues());
        Assert.True(_reg.KeyExists(RegistryHive.HKCU, NewKey));

        engine.Revert(TwoValues());
        Assert.False(_reg.KeyExists(RegistryHive.HKCU, NewKey));
    }

    [Fact]
    public void Applying_twice_keeps_the_original_snapshot()
    {
        _reg.Seed(RegistryHive.HKCU, AdvKey, "HideFileExt", RegistryKind.DWord, "1");
        var engine = CreateEngine();

        engine.Apply(FileExt());
        engine.Apply(FileExt());
        engine.Revert(FileExt());

        Assert.Equal("1", _reg.Read(RegistryHive.HKCU, AdvKey, "HideFileExt")!.Data);
    }

    [Fact]
    public void Journal_survives_a_restart_of_the_app()
    {
        _reg.Seed(RegistryHive.HKCU, AdvKey, "HideFileExt", RegistryKind.DWord, "5");
        CreateEngine().Apply(FileExt());

        // Neue Engine mit neuem JournalStore liest die Datei frisch ein
        Assert.True(CreateEngine().Revert(FileExt()).Success);
        Assert.Equal("5", _reg.Read(RegistryHive.HKCU, AdvKey, "HideFileExt")!.Data);
    }

    [Fact]
    public void Failed_write_rolls_back_everything_already_changed()
    {
        _reg.Seed(RegistryHive.HKCU, NewKey, "A", RegistryKind.DWord, "0");
        _reg.FailOnName = "B";
        var engine = CreateEngine();

        var result = engine.Apply(TwoValues());

        Assert.False(result.Success);
        Assert.Equal("0", _reg.Read(RegistryHive.HKCU, NewKey, "A")!.Data);
        Assert.Null(engine.Journal.Get("test.two"));
    }

    [Fact]
    public void Failed_verification_rolls_back()
    {
        _reg.Seed(RegistryHive.HKCU, NewKey, "A", RegistryKind.DWord, "0");
        _reg.IgnoreWritesTo = "B";
        var engine = CreateEngine();

        Assert.False(engine.Apply(TwoValues()).Success);
        Assert.Equal("0", _reg.Read(RegistryHive.HKCU, NewKey, "A")!.Data);
    }

    [Fact]
    public void Revert_without_journal_uses_windows_default()
    {
        // Tweak wurde außerhalb der App gesetzt
        _reg.Seed(RegistryHive.HKCU, AdvKey, "HideFileExt", RegistryKind.DWord, "0");
        var engine = CreateEngine();

        Assert.Equal(TweakStatus.Applied, engine.GetStatus(FileExt()));
        Assert.True(engine.Revert(FileExt()).Success);
        Assert.Equal("1", _reg.Read(RegistryHive.HKCU, AdvKey, "HideFileExt")!.Data);
    }

    [Fact]
    public void Partial_status_when_windows_reset_one_value()
    {
        var engine = CreateEngine();
        engine.Apply(TwoValues());
        _reg.Seed(RegistryHive.HKCU, NewKey, "B", RegistryKind.String, "aus");

        Assert.Equal(TweakStatus.Partial, engine.GetStatus(TwoValues()));
    }

    [Fact]
    public void Tweak_outside_build_range_is_not_available_and_changes_nothing()
    {
        var win11Only = FileExt();
        win11Only.MinBuild = 22000;
        var engine = CreateEngine(build: 19045);

        Assert.Equal(TweakStatus.NotAvailable, engine.GetStatus(win11Only));
        Assert.False(engine.Apply(win11Only).Success);
        Assert.Equal(0, _reg.Writes);
    }

    [Fact]
    public void Service_tweak_needs_existing_service_and_restores_start_type()
    {
        const string svc = @"SYSTEM\CurrentControlSet\Services\TabletInputService";
        var tweak = new TweakDefinition
        {
            Id = "services.tablet.off",
            Title = new() { De = "x", En = "x" },
            Description = new() { De = "x", En = "x" },
            Actions = { new ServiceAction { Name = "TabletInputService", StartType = ServiceStartType.Disabled } },
        };

        var engine = CreateEngine();
        Assert.Equal(TweakStatus.NotAvailable, engine.GetStatus(tweak));

        _reg.Seed(RegistryHive.HKLM, svc, "Start", RegistryKind.DWord, "3");
        Assert.True(engine.Apply(tweak).Success);
        Assert.Equal("4", _reg.Read(RegistryHive.HKLM, svc, "Start")!.Data);

        engine.Revert(tweak);
        Assert.Equal("3", _reg.Read(RegistryHive.HKLM, svc, "Start")!.Data);
        Assert.Null(_reg.Read(RegistryHive.HKLM, svc, "DelayedAutostart"));
    }

    [Theory]
    [InlineData(RegistryKind.DWord, RegistryKind.DWord, "0", "0", true)]
    [InlineData(RegistryKind.DWord, RegistryKind.DWord, "4294967295", "4294967295", true)]
    [InlineData(RegistryKind.DWord, RegistryKind.String, "0", "0", false)]
    [InlineData(RegistryKind.String, RegistryKind.String, "An", "an", false)]
    public void Values_are_compared_by_type(RegistryKind target, RegistryKind actual, string actualData, string targetData, bool expected)
    {
        Assert.Equal(expected, TweakEngine.ValuesEqual(target, new RegistryValue(actual, actualData), targetData));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public class CatalogTests
{
    private static Stream Json(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private const string Valid = """
        [{ "id": "ui.test.one", "category": "Interface", "risk": "Low",
           "title": { "de": "T", "en": "T" }, "description": { "de": "D", "en": "D" },
           "actions": [ { "type": "registry", "hive": "HKCU", "key": "Software\\X", "name": "A", "kind": "DWord", "value": "1" } ] }]
        """;

    [Fact]
    public void Valid_catalog_loads()
    {
        var tweaks = CatalogLoader.Load(new[] { ("a.json", Json(Valid)) });
        var action = Assert.IsType<RegistryAction>(Assert.Single(Assert.Single(tweaks).Actions));
        Assert.Equal("1", action.Value);
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        Assert.Throws<CatalogException>(() => CatalogLoader.Load(new[] { ("a.json", Json(Valid)), ("b.json", Json(Valid)) }));
    }

    [Theory]
    [InlineData("\"value\": \"1\"", "\"value\": \"abc\"")]
    [InlineData("\"en\": \"T\"", "\"en\": \"\"")]
    [InlineData("\"key\": \"Software\\\\X\"", "\"key\": \"HKEY_CURRENT_USER\\\\X\"")]
    [InlineData("\"id\": \"ui.test.one\"", "\"id\": \"Ohne Punkt\"")]
    public void Invalid_entries_are_rejected(string from, string to)
    {
        Assert.Throws<CatalogException>(() => CatalogLoader.Load(new[] { ("a.json", Json(Valid.Replace(from, to))) }));
    }

    /// <summary>Der echte Katalog im Repo muss immer gültig sein, sonst schlägt der Build fehl.</summary>
    [Fact]
    public void Repository_catalog_is_valid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "catalog"), "*.json")
            .Select(f => (Path.GetFileName(f), (Stream)File.OpenRead(f)))
            .ToList();

        var tweaks = CatalogLoader.Load(files);
        Assert.InRange(tweaks.Count, 50, 140);

        // Jede Kategorie ist vertreten, Dienst- und Energieplan-Tweaks sind nie "Niedrig" ohne Grund
        Assert.Equal(Enum.GetValues<TweakCategory>().Length, tweaks.Select(t => t.Category).Distinct().Count());
        Assert.All(tweaks.Where(t => t.Actions.OfType<PowerPlanAction>().Any()), t => Assert.Equal(TweakRisk.Medium, t.Risk));

        // Policy-Werte ohne Standard werden beim Rückgängig gelöscht: nur unter Policies erlaubt oder bewusst gewählt
        var allowedWithoutDefault = new[] { "Policies", "Serialize", "Siuf", "GraphicsDrivers", "PowerThrottling", "UserGpuPreferences", "International", "CLSID", "Advanced", @"CurrentVersion\Search", @"Session Manager\kernel", @"Shell Extensions\Blocked" };
        foreach (var action in tweaks.SelectMany(t => t.Actions).OfType<RegistryAction>().Where(a => a.Default is null))
        {
            Assert.Contains(allowedWithoutDefault, part => action.Key.Contains(part, StringComparison.OrdinalIgnoreCase));
        }
    }
}

internal sealed class FakePowerPlans : IPowerPlans
{
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public HashSet<Guid> Schemes { get; } = new() { TweakEngine.BalancedScheme };

    public Guid Active { get; set; } = TweakEngine.BalancedScheme;

    public bool IsSupported { get; set; } = true;

    public Guid GetActive() => Active;

    public bool Exists(Guid scheme) => Schemes.Contains(scheme);

    public Guid Duplicate(Guid template)
    {
        var created = Guid.NewGuid();
        Schemes.Add(created);
        return created;
    }

    public void SetActive(Guid scheme)
    {
        if (!Schemes.Contains(scheme))
        {
            throw new IOException("Plan fehlt");
        }

        Active = scheme;
    }

    public void Delete(Guid scheme) => Schemes.Remove(scheme);
}

public sealed class PowerPlanAndAccountTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-power-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRegistry _reg = new();
    private readonly FakePowerPlans _power = new();

    private static TweakDefinition Ultimate() => new()
    {
        Id = "perf.powerplan.ultimate",
        Title = new() { De = "x", En = "x" },
        Description = new() { De = "x", En = "x" },
        Actions = { new PowerPlanAction { Template = FakePowerPlans.Ultimate.ToString() } },
    };

    private TweakEngine CreateEngine() => new(_reg, new JournalStore(Path.Combine(_dir, "j.json")), new FakeNotifier(), 26100, _power);

    [Fact]
    public void Missing_plan_is_created_activated_and_removed_on_revert()
    {
        var engine = CreateEngine();

        Assert.True(engine.Apply(Ultimate()).Success);
        Assert.Equal(2, _power.Schemes.Count);
        Assert.Equal(TweakStatus.Applied, engine.GetStatus(Ultimate()));

        Assert.True(engine.Revert(Ultimate()).Success);
        Assert.Equal(TweakEngine.BalancedScheme, _power.Active);
        Assert.Single(_power.Schemes);
    }

    [Fact]
    public void Existing_plan_is_used_and_kept_on_revert()
    {
        _power.Schemes.Add(FakePowerPlans.Ultimate);
        var engine = CreateEngine();

        engine.Apply(Ultimate());
        Assert.Equal(FakePowerPlans.Ultimate, _power.Active);

        engine.Revert(Ultimate());
        Assert.Contains(FakePowerPlans.Ultimate, _power.Schemes);
        Assert.Equal(TweakEngine.BalancedScheme, _power.Active);
    }

    [Fact]
    public void Reapplying_does_not_create_a_second_plan()
    {
        var engine = CreateEngine();
        engine.Apply(Ultimate());
        engine.Apply(Ultimate());

        Assert.Equal(2, _power.Schemes.Count);
    }

    [Fact]
    public void Modern_standby_makes_power_tweak_unavailable()
    {
        _power.IsSupported = false;
        Assert.Equal(TweakStatus.NotAvailable, CreateEngine().GetStatus(Ultimate()));
    }

    [Fact]
    public void Foreign_account_blocks_only_user_tweaks()
    {
        var user = new TweakDefinition
        {
            Id = "ui.a.b",
            Actions = { new RegistryAction { Hive = RegistryHive.HKCU, Key = @"Software\X", Name = "A", Kind = RegistryKind.DWord, Value = "1" } },
        };
        var machine = new TweakDefinition
        {
            Id = "net.a.b",
            Actions = { new RegistryAction { Hive = RegistryHive.HKLM, Key = @"SOFTWARE\X", Name = "A", Kind = RegistryKind.DWord, Value = "1" } },
        };

        var engine = CreateEngine();
        engine.UserHiveAllowed = false;

        Assert.True(engine.IsBlockedByAccount(user));
        Assert.Equal(TweakStatus.NotAvailable, engine.GetStatus(user));
        Assert.False(engine.Apply(user).Success);
        Assert.False(engine.IsBlockedByAccount(machine));
        Assert.True(engine.Apply(machine).Success);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class SelfTestRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-self-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRegistry _reg = new();

    private static TweakDefinition Reg(string id, string name, string value = "0", int minBuild = 19045) => new()
    {
        Id = id,
        MinBuild = minBuild,
        Actions = { new RegistryAction { Hive = RegistryHive.HKCU, Key = @"Software\Test", Name = name, Kind = RegistryKind.DWord, Value = value } },
    };

    [Fact]
    public void Reports_ok_unavailable_skipped_and_failed()
    {
        _reg.Seed(RegistryHive.HKCU, @"Software\Test", "A", RegistryKind.DWord, "1");
        _reg.IgnoreWritesTo = "C";

        var userJournal = new JournalStore(Path.Combine(_dir, "user.json"));
        userJournal.Set(new JournalEntry { TweakId = "t.skip" });
        var engine = new TweakEngine(_reg, new JournalStore(Path.Combine(_dir, "test.json")), new FakeNotifier(), 19045);

        var results = new SelfTestRunner(engine, userJournal).Run(new[]
        {
            Reg("t.ok", "A"),
            Reg("t.win11", "B", minBuild: 22000),
            Reg("t.skip", "D"),
            Reg("t.fail", "C"),
        }).ToDictionary(r => r.TweakId);

        Assert.Equal(SelfTestOutcome.Ok, results["t.ok"].Outcome);
        Assert.True(results["t.ok"].StateRestored);
        Assert.Equal("1", _reg.Read(RegistryHive.HKCU, @"Software\Test", "A")!.Data);

        Assert.Equal(SelfTestOutcome.NotAvailable, results["t.win11"].Outcome);
        Assert.Contains("22000", results["t.win11"].Note);

        Assert.Equal(SelfTestOutcome.Skipped, results["t.skip"].Outcome);
        Assert.Null(_reg.Read(RegistryHive.HKCU, @"Software\Test", "D"));

        Assert.Equal(SelfTestOutcome.Failed, results["t.fail"].Outcome);
    }

    [Fact]
    public void Report_lists_failures_first_with_summary()
    {
        var report = SelfTestRunner.FormatReport(new[]
        {
            new SelfTestResult("a.ok", SelfTestOutcome.Ok, TweakStatus.NotApplied, TweakStatus.Applied, TweakStatus.NotApplied, true, ""),
            new SelfTestResult("b.bad", SelfTestOutcome.Failed, TweakStatus.NotApplied, TweakStatus.NotApplied, TweakStatus.NotApplied, true, "Kontrolle"),
        }, "Windows 11", "0.7.0", new DateTime(2026, 11, 4, 19, 0, 0));

        Assert.Contains("OK: 1 · Fehler: 1", report);
        Assert.True(report.IndexOf("== FEHLER ==", StringComparison.Ordinal) < report.IndexOf("== OK ==", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

internal sealed class ThrowingRegistry : IRegistry
{
    public bool KeyExists(RegistryHive hive, string key) => throw new InvalidOperationException("kaputt");

    public RegistryValue? Read(RegistryHive hive, string key, string name) => null;

    public void Write(RegistryHive hive, string key, string name, RegistryKind kind, string data) { }

    public void Delete(RegistryHive hive, string key, string name) { }

    public void DeleteKeyIfEmpty(RegistryHive hive, string key) { }

    public IReadOnlyList<string> SubKeys(RegistryHive hive, string key) => Array.Empty<string>();
}

public sealed class SelfTestRobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-robust-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Unexpected_exception_in_one_tweak_is_reported_not_fatal()
    {
        var engine = new TweakEngine(new ThrowingRegistry(), new JournalStore(Path.Combine(_dir, "t.json")), new FakeNotifier(), 26100);
        var tweak = new TweakDefinition { Id = "services.x.off", Actions = { new ServiceAction { Name = "X" } } };

        var result = Assert.Single(new SelfTestRunner(engine, new JournalStore(Path.Combine(_dir, "u.json"))).Run(new[] { tweak }));
        Assert.Equal(SelfTestOutcome.Failed, result.Outcome);
        Assert.Contains("InvalidOperationException", result.Note);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
