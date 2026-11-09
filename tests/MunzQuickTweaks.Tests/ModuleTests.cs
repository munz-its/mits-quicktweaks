using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tweaks;
using Xunit;

namespace MunzQuickTweaks.Tests;

public class ProfileTests
{
    private static IReadOnlyList<TweakDefinition> RepoCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "catalog"), "*.json")
            .Select(f => (Path.GetFileName(f), (Stream)File.OpenRead(f)));
        return CatalogLoader.Load(files);
    }

    [Theory]
    [InlineData(ProfileKind.Recommended)]
    [InlineData(ProfileKind.Gaming)]
    [InlineData(ProfileKind.Privacy)]
    public void Every_profile_tweak_exists_in_the_catalog(ProfileKind kind)
    {
        var catalog = RepoCatalog();
        var ids = catalog.Select(t => t.Id).ToHashSet();

        var profile = ProfileCatalog.Get(kind, catalog);
        Assert.NotEmpty(profile.TweakIds);
        Assert.All(profile.TweakIds, id => Assert.Contains(id, ids));
        Assert.Equal(profile.TweakIds.Count, profile.TweakIds.Distinct().Count());
    }

    [Fact]
    public void Recommended_contains_only_low_risk()
    {
        var catalog = RepoCatalog().ToDictionary(t => t.Id);
        Assert.All(ProfileCatalog.Recommended.TweakIds, id => Assert.Equal(TweakRisk.Low, catalog[id].Risk));
    }

    [Fact]
    public void Medium_tweaks_in_profiles_are_exactly_the_agreed_ones()
    {
        var catalog = RepoCatalog();
        var byId = catalog.ToDictionary(t => t.Id);

        var gamingMedium = ProfileCatalog.Gaming.TweakIds.Where(id => byId[id].Risk == TweakRisk.Medium).OrderBy(x => x);
        Assert.Equal(new[] { "gaming.hags.on", "gaming.powerthrottling.off", "net.throttling.off", "perf.powerplan.ultimate" }, gamingMedium);

        var privacy = ProfileCatalog.Privacy(catalog).TweakIds;
        Assert.Equal(new[] { "privacy.copilot.off", "privacy.recall.off" }, privacy.Where(id => byId[id].Risk == TweakRisk.Medium).OrderBy(x => x));
        Assert.DoesNotContain("privacy.location.off", privacy);
        Assert.Contains("services.diagtrack.off", privacy);
    }
}

public class DebloatTests
{
    [Fact]
    public void Only_installed_apps_are_matched_with_all_their_packages()
    {
        var installed = new[]
        {
            new InstalledPackage("Microsoft.BingNews", "Microsoft.BingNews_4.1_x64__8wekyb3d8bbwe"),
            new InstalledPackage("Microsoft.BingNews", "Microsoft.BingNews_4.1_neutral__8wekyb3d8bbwe"),
            new InstalledPackage("Microsoft.WindowsStore", "Microsoft.WindowsStore_22_x64__8wekyb3d8bbwe"),
        };

        var match = Assert.Single(DebloatCatalog.Match(installed));
        Assert.Equal("Microsoft.BingNews", match.App.Package);
        Assert.Equal(2, match.Packages.Count);
    }

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.XboxIdentityProvider")]
    [InlineData("Microsoft.Xbox.TCUI")]
    [InlineData("Microsoft.Windows.Photos")]
    [InlineData("Microsoft.WindowsCalculator")]
    [InlineData("Microsoft.MicrosoftEdge.Stable")]
    public void Essential_packages_are_never_in_the_list(string package)
    {
        Assert.DoesNotContain(DebloatCatalog.All, a => a.Package == package);
    }

    [Fact]
    public void Preselection_matches_the_decision()
    {
        Assert.All(DebloatCatalog.All, a => Assert.Equal(a.Group != DebloatGroup.Other, a.SelectedByDefault));
        Assert.Equal(DebloatCatalog.All.Count, DebloatCatalog.All.Select(a => a.Package).Distinct().Count());
    }
}

public class StartupApprovedTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, false)]
    public void Reads_task_manager_format(byte[]? data, bool expected)
    {
        Assert.Equal(expected, StartupApproved.IsEnabled(data));
    }

    [Fact]
    public void Created_values_round_trip()
    {
        var disabled = StartupApproved.Create(false, new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(12, disabled.Length);
        Assert.False(StartupApproved.IsEnabled(disabled));
        Assert.NotEqual(0L, BitConverter.ToInt64(disabled, 4));
        Assert.True(StartupApproved.IsEnabled(StartupApproved.Create(true, DateTime.UtcNow)));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Steam\\steam.exe\" -silent", "C:\\Program Files\\Steam\\steam.exe")]
    [InlineData("C:\\Tools\\tool.exe --tray", "C:\\Tools\\tool.exe")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    public void Extracts_program_path(string command, string expected)
    {
        Assert.Equal(expected, StartupApproved.ExtractPath(command));
    }
}

public sealed class FolderCleanerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-clean-" + Guid.NewGuid().ToString("N"));

    public FolderCleanerTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
    }

    private string Create(string name, int bytes, TimeSpan age)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    [Fact]
    public void Only_old_files_are_deleted_and_counted()
    {
        var old = Create("alt.tmp", 1000, TimeSpan.FromDays(3));
        var oldSub = Create(Path.Combine("sub", "alt2.tmp"), 500, TimeSpan.FromDays(3));
        var fresh = Create("neu.tmp", 2000, TimeSpan.FromMinutes(5));

        Assert.Equal(1500, FolderCleaner.Measure(_dir, TimeSpan.FromHours(24)).Bytes);

        var result = FolderCleaner.Clean(_dir, TimeSpan.FromHours(24));
        Assert.Equal(2, result.Files);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(oldSub));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Pattern_limits_to_matching_files_without_recursion()
    {
        Create("thumbcache_256.db", 100, TimeSpan.Zero);
        Create("iconcache_16.db", 100, TimeSpan.Zero);

        var result = FolderCleaner.Clean(_dir, TimeSpan.Zero, "thumbcache_*.db");
        Assert.Equal(1, result.Files);
        Assert.True(File.Exists(Path.Combine(_dir, "iconcache_16.db")));
    }

    [Fact]
    public void Missing_folder_is_not_an_error()
    {
        Assert.Equal(CleanupResult.Empty, FolderCleaner.Clean(Path.Combine(_dir, "gibtsnicht"), TimeSpan.Zero));
    }

    [Fact]
    public void Empty_subfolders_are_removed_after_cleaning()
    {
        Create(Path.Combine("sub", "x.tmp"), 10, TimeSpan.FromDays(2));
        FolderCleaner.Clean(_dir, TimeSpan.FromHours(1));
        Assert.False(Directory.Exists(Path.Combine(_dir, "sub")));
        Assert.True(Directory.Exists(_dir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public class TweakSelectionTests
{
    [Fact]
    public void Round_trip_keeps_ids_and_removes_duplicates()
    {
        var json = new TweakSelection { AppVersion = "0.8.0", Tweaks = new() { "ui.a.b", "ui.a.b", "privacy.c.d", " " } }.ToJson();
        var parsed = TweakSelection.Parse(json);
        Assert.Equal(new[] { "ui.a.b", "privacy.c.d" }, parsed.Tweaks);
    }

    [Theory]
    [InlineData("{ kaputt")]
    [InlineData("{\"format\":\"andere-app\",\"tweaks\":[\"x\"]}")]
    [InlineData("{\"format\":\"munz-quicktweaks-selection\",\"formatVersion\":2,\"tweaks\":[]}")]
    public void Foreign_or_broken_files_are_rejected(string json)
    {
        Assert.Throws<FormatException>(() => TweakSelection.Parse(json));
    }
}

public class TweakDetailsTests
{
    [Fact]
    public void Describes_registry_service_and_build_in_german()
    {
        var tweak = new TweakDefinition
        {
            Id = "test.x.y",
            MinBuild = 22000,
            Actions =
            {
                new RegistryAction { Hive = RegistryHive.HKCU, Key = @"Software\X", Name = "", Kind = RegistryKind.String, Value = "" },
                new ServiceAction { Name = "WSearch", StartType = ServiceStartType.Disabled, Default = ServiceStartType.AutomaticDelayed },
            },
        };

        var lines = TweakDetails.Describe(tweak, "de");
        Assert.Contains(@"HKCU\Software\X\(Standard) = """" (String) · Windows-Standard: Wert fehlt", lines);
        Assert.Contains("Dienst WSearch: Starttyp Deaktiviert · Windows-Standard: Automatisch (verzögert)", lines);
        Assert.Contains("Ab Windows-Build 22000", lines);
    }
}
