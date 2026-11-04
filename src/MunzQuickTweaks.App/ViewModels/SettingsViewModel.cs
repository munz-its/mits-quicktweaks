using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Settings;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Sprache, Animationen und Info mit Impressum.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;

    public SettingsViewModel(SettingsService settings, LocalizationService localization)
        : base("Nav.Settings")
    {
        _settings = settings;
        _localization = localization;
        _language = settings.Current.Language;
        _animationsEnabled = settings.Current.AnimationsEnabled;

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        Version = version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public string Version { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemLanguage), nameof(IsGerman), nameof(IsEnglish))]
    private AppLanguage _language;

    [ObservableProperty]
    private bool _animationsEnabled;

    public bool IsSystemLanguage
    {
        get => Language == AppLanguage.System;
        set { if (value) Language = AppLanguage.System; }
    }

    public bool IsGerman
    {
        get => Language == AppLanguage.German;
        set { if (value) Language = AppLanguage.German; }
    }

    public bool IsEnglish
    {
        get => Language == AppLanguage.English;
        set { if (value) Language = AppLanguage.English; }
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        _settings.Current.Language = value;
        _settings.Save();
        _localization.Apply(value);
    }

    partial void OnAnimationsEnabledChanged(bool value)
    {
        Controls.UiMotion.Enabled = value;
        _settings.Current.AnimationsEnabled = value;
        _settings.Save();
    }
}
