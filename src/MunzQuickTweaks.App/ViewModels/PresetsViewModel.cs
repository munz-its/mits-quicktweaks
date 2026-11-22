using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.ViewModels;

public sealed partial class PresetCard : ObservableObject
{
    public PresetCard(ProfileKind kind, string icon)
    {
        Kind = kind;
        Icon = icon;
    }

    public ProfileKind Kind { get; }

    public string Icon { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    [ObservableProperty]
    private string _info = string.Empty;

    [ObservableProperty]
    private string _extras = string.Empty;

    public bool IsReset => Kind == ProfileKind.ResetAll;
}

/// <summary>Alle Voreinstellungen: ein Klick, Vorschau mit Häkchen, dann anwenden.</summary>
public sealed partial class PresetsViewModel : PageViewModel
{
    private readonly ProfileService _profiles;
    private readonly TweakService _tweaks;
    private readonly LocalizationService _localization;
    private readonly UpdateService _app;
    private readonly Microsoft.Extensions.Logging.ILogger<PresetsViewModel> _log;

    public PresetsViewModel(ProfileService profiles, TweakService tweaks, LocalizationService localization, UpdateService app,
        Microsoft.Extensions.Logging.ILogger<PresetsViewModel> log)
        : base("Nav.Presets")
    {
        _app = app;
        _log = log;
        _profiles = profiles;
        _tweaks = tweaks;
        _localization = localization;
        Cards = new ObservableCollection<PresetCard>(new[]
        {
            new PresetCard(ProfileKind.Recommended, "00100/01110/11111/01110/01010"),
            new PresetCard(ProfileKind.Gaming, "01110/11111/10101/11111/01010"),
            new PresetCard(ProfileKind.MaxPerformance, "00110/01100/11111/00110/01100"),
            new PresetCard(ProfileKind.Privacy, "01110/10001/11111/11011/11111"),
            new PresetCard(ProfileKind.Telemetry, "10001/01010/00100/01010/10001"),
            new PresetCard(ProfileKind.Battery, "01110/11111/10001/11111/11111"),
            new PresetCard(ProfileKind.Office, "11111/10001/11111/10001/11111"),
            new PresetCard(ProfileKind.ResetAll, "01110/10101/10111/10001/01110"),
        });
        localization.LanguageChanged += (_, _) => _ = RefreshAsync();
    }

    public ObservableCollection<PresetCard> Cards { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ExportCommand), nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    public override Task OnNavigatedToAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        var statuses = await Task.Run(() => _tweaks.Tweaks.ToDictionary(t => t.Id, t => _tweaks.GetStatus(t)));
        foreach (var card in Cards)
        {
            card.Name = _localization.Get($"Profile.{card.Kind}");
            card.Hint = _localization.Get($"Profile.{card.Kind}.Hint");
            if (card.IsReset)
            {
                card.Info = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.ActiveCount"), statuses.Values.Count(s => s == TweakStatus.Applied));
                card.Extras = _localization.Get("Presets.ResetExtras");
                continue;
            }

            var profile = ProfileCatalog.Get(card.Kind, _tweaks.Tweaks);
            var ids = profile.TweakIds.Where(id => statuses.TryGetValue(id, out var s) && s != TweakStatus.NotAvailable).ToList();
            var medium = ids.Count(id => _tweaks.Find(id)?.Risk == TweakRisk.Medium);
            card.Info = medium == 0
                ? string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Count"), ids.Count)
                : string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.CountMedium"), ids.Count, medium);

            var extras = new List<string>();
            if (profile.Mode is { } mode) extras.Add(_localization.Get($"Power.{mode}"));
            if (profile.StartupApps.Count > 0) extras.Add(_localization.Get("Presets.ExtraStartup"));
            if (profile.Debloat.Count > 0) extras.Add(_localization.Get("Presets.ExtraDebloat"));
            card.Extras = extras.Count == 0 ? string.Empty : "+ " + string.Join(" · ", extras);
        }
    }

    private bool CanApply() => !IsBusy;

    private string FileFilter => $"{_localization.Get("Profile.FileType")} (*{ProfileFile.Extension})|*{ProfileFile.Extension}";

    /// <summary>Aktuellen Stand als .mqtprofile speichern, z. B. um ihn auf andere PCs zu übertragen.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ExportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = FileFilter,
            DefaultExt = ProfileFile.Extension,
            AddExtension = true,
            FileName = ProfileFile.SafeFileName(string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.DefaultName"), Environment.MachineName)),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var profile = await _profiles.ExportCurrentAsync(Path.GetFileNameWithoutExtension(dialog.FileName), _app.CurrentVersion);
            if (profile.IsEmpty)
            {
                Status = _localization.Get("Profile.ExportEmpty");
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, profile.Serialize());
            _log.LogInformation("Profil exportiert: {Tweaks} Tweaks, {Apps} Apps, {Startup} Autostart", profile.Tweaks.Count, profile.RemoveApps.Count, profile.DisableStartup.Count);
            Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Exported"), Path.GetFileName(dialog.FileName),
                profile.Tweaks.Count + profile.RemoveApps.Count + profile.DisableStartup.Count + (profile.PowerMode is null ? 0 : 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Profil aus einer Datei: erst Vorschau mit Häkchen, dann anwenden.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FileFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ImportFileAsync(dialog.FileName);
    }

    public async Task ImportFileAsync(string path)
    {
        IsBusy = true;
        try
        {
            if (new FileInfo(path).Length > ProfileFile.MaxBytes)
            {
                Status = _localization.Get("Profile.ImportInvalid");
                return;
            }

            var (file, error) = ProfileFile.Parse(await File.ReadAllTextAsync(path));
            if (file is null)
            {
                Status = _localization.Get(error == ProfileFileError.TooNew ? "Profile.ImportTooNew" : "Profile.ImportInvalid");
                return;
            }

            var (profile, unknown) = file.ToProfile(_tweaks.Tweaks.Select(t => t.Id));
            var name = file.Name.Length > 0 ? file.Name : Path.GetFileNameWithoutExtension(path);
            var intro = _localization.Get("Profile.ImportIntro");
            if (unknown > 0)
            {
                intro += " " + string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.ImportUnknown"), unknown);
            }

            _log.LogInformation("Profil importiert: {Name}, {Tweaks} Tweaks, {Unknown} unbekannt", name, profile.TweakIds.Count, unknown);
            Status = await _profiles.RunImportedAsync(profile, name, intro, text => Status = text);
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync(PresetCard card)
    {
        IsBusy = true;
        try
        {
            Status = await _profiles.RunProfileAsync(card.Kind, text => Status = text);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
