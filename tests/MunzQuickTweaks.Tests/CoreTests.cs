using System.Globalization;
using System.Xml.Linq;
using MunzQuickTweaks.Core.Machine;
using MunzQuickTweaks.Core.Settings;
using Xunit;

namespace MunzQuickTweaks.Tests;

public class LanguageResolverTests
{
    [Theory]
    [InlineData(AppLanguage.German, "en-US", "de")]
    [InlineData(AppLanguage.English, "de-DE", "en")]
    [InlineData(AppLanguage.System, "de-DE", "de")]
    [InlineData(AppLanguage.System, "de-AT", "de")]
    [InlineData(AppLanguage.System, "en-GB", "en")]
    [InlineData(AppLanguage.System, "fr-FR", "en")]
    public void Resolves_expected_language(AppLanguage setting, string culture, string expected)
    {
        Assert.Equal(expected, LanguageResolver.Resolve(setting, new CultureInfo(culture)));
    }
}

public class OsNamingTests
{
    [Theory]
    [InlineData(19045, true)]
    [InlineData(19044, false)]
    [InlineData(22000, true)]
    [InlineData(26100, true)]
    [InlineData(17763, false)]
    public void Supports_only_win10_22h2_and_win11(int build, bool expected)
    {
        Assert.Equal(expected, OsNaming.IsSupported(build));
    }

    [Fact]
    public void Windows11_is_detected_by_build_not_product_name()
    {
        Assert.Equal("Windows 11 Home 24H2 (Build 26100.2314)", OsNaming.GetDisplayName(26100, 2314, "Core", "24H2"));
        Assert.Equal("Windows 10 Pro 22H2 (Build 19045.6456)", OsNaming.GetDisplayName(19045, 6456, "Professional", "22H2"));
    }

    [Fact]
    public void Unknown_edition_is_shown_as_is()
    {
        Assert.Equal("ServerStandard", OsNaming.GetEditionName("ServerStandard"));
        Assert.Equal(string.Empty, OsNaming.GetEditionName(null));
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = new SettingsStore(Path.Combine(_dir, "settings.json")).Load();
        Assert.Equal(AppLanguage.System, settings.Language);
        Assert.True(settings.AnimationsEnabled);
    }

    [Fact]
    public void Saved_settings_are_loaded_again()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(new AppSettings { Language = AppLanguage.English, AnimationsEnabled = false });

        var loaded = store.Load();
        Assert.Equal(AppLanguage.English, loaded.Language);
        Assert.False(loaded.AnimationsEnabled);
    }

    [Fact]
    public void Broken_file_gives_defaults_instead_of_crash()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ kaputt");

        Assert.Equal(AppLanguage.System, new SettingsStore(path).Load().Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public class StringResourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void German_and_English_have_the_same_keys()
    {
        var german = ReadKeys("Strings.de.xaml");
        var english = ReadKeys("Strings.en.xaml");

        Assert.Empty(german.Except(english));
        Assert.Empty(english.Except(german));
    }

    private static HashSet<string> ReadKeys(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "MunzQuickTweaks.App", "Resources", fileName);
        return XDocument.Load(path).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .OfType<string>()
            .ToHashSet();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden");
    }
}
