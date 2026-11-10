using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Settings;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Sprache, Animationen und Info mit Impressum.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly TweakService _tweaks;
    private readonly BatchApplyService _batch;

    public SettingsViewModel(SettingsService settings, LocalizationService localization, TweakService tweaks, BatchApplyService batch, UpdateService updates)
        : base("Nav.Settings")
    {
        _updates = updates;
        _checkForUpdates = settings.Current.CheckForUpdates;
        _checkResetAfterUpdates = settings.Current.CheckResetAfterUpdates;
        _settings = settings;
        _localization = localization;
        _tweaks = tweaks;
        _batch = batch;
        _language = settings.Current.Language;
        _animationsEnabled = settings.Current.AnimationsEnabled;

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        Version = version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public string Version { get; }

    private readonly UpdateService _updates;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    private bool _isChecking;

    [ObservableProperty]
    private bool _checkResetAfterUpdates;

    partial void OnCheckResetAfterUpdatesChanged(bool value)
    {
        _settings.Current.CheckResetAfterUpdates = value;
        _settings.Save();
        App.EnsureResetTask(value);
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        _settings.Current.CheckForUpdates = value;
        _settings.Save();
    }

    private bool CanCheck() => !IsChecking;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        UpdateStatus = _localization.Get("Update.Checking");
        try
        {
            var (newer, error) = await _updates.CheckAsync();
            UpdateStatus = error is not null
                ? $"{_localization.Get("Update.Failed")} {error}"
                : newer is not null
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Update.Available"), newer.Version, _updates.CurrentVersion)
                    : _localization.Get("Update.Current");
        }
        finally
        {
            IsChecking = false;
        }
    }

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _transferStatus = string.Empty;

    private bool CanTransfer() => !IsBusy;

    /// <summary>Aktive Tweaks als Datei speichern, z. B. für weitere PCs.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task ExportAsync()
    {
        var active = await Task.Run(() => _tweaks.Tweaks
            .Where(t => _tweaks.GetStatus(t) == Core.Tweaks.TweakStatus.Applied)
            .Select(t => t.Id)
            .ToList());

        if (active.Count == 0)
        {
            TransferStatus = _localization.Get("Transfer.NothingActive");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"MunzQuickTweaks-Auswahl-{DateTime.Now:yyyyMMdd}.json",
            Filter = "Munz Quick Tweaks (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var selection = new Core.Tweaks.TweakSelection { AppVersion = Version, Created = DateTimeOffset.Now, Tweaks = active };
        await File.WriteAllTextAsync(dialog.FileName, selection.ToJson());
        TransferStatus = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Transfer.Exported"), active.Count);
    }

    /// <summary>Datei einlesen und die darin enthaltenen Tweaks nach Rückfrage anwenden.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Munz Quick Tweaks (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Core.Tweaks.TweakSelection selection;
        try
        {
            selection = Core.Tweaks.TweakSelection.Parse(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (FormatException ex)
        {
            TransferStatus = ex.Message;
            return;
        }

        IsBusy = true;
        try
        {
            var todo = await _batch.FilterTodoAsync(selection.Tweaks);
            if (todo.Count == 0)
            {
                TransferStatus = _localization.Get("Transfer.AllActive");
                return;
            }

            var heading = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Transfer.Confirm"), todo.Count);
            var result = await _batch.ConfirmAndApplyAsync(todo, heading, text => TransferStatus = text, "Import");
            TransferStatus = result.Cancelled
                ? string.Empty
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Reset.Done"), result.Applied, result.Failed);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
