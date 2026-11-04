using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tweaks;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Startseite mit Systemdaten. Die Daten werden einmal im Hintergrund gelesen.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly SystemInfoProvider _provider;
    private readonly TweakService _tweaks;
    private readonly IRestorePointService _restorePoints;
    private readonly LocalizationService _localization;
    private readonly HistoryStore _history;
    private bool _loaded;

    public DashboardViewModel(SystemInfoProvider provider, TweakService tweaks, IRestorePointService restorePoints, LocalizationService localization, HistoryStore history)
        : base("Nav.Dashboard")
    {
        _provider = provider;
        _tweaks = tweaks;
        _restorePoints = restorePoints;
        _localization = localization;
        _history = history;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyProfileCommand))]
    private bool _isApplyingProfile;

    [ObservableProperty]
    private string _profileStatus = string.Empty;

    private bool CanApplyProfile() => !IsApplyingProfile;

    /// <summary>Wendet ein Profil an. Eine Rückfrage zeigt vorher alle Tweaks, Risiko mittel ist markiert.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyProfile))]
    private async Task ApplyProfileAsync(string kindName)
    {
        var kind = Enum.Parse<ProfileKind>(kindName);
        var appName = _localization.Get("App.Name");
        var profileName = _localization.Get($"Profile.{kind}");

        if (kind == ProfileKind.ResetAll)
        {
            if (MessageBox.Show(_localization.Get("Restore.ConfirmRevertAll"), appName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            IsApplyingProfile = true;
            try
            {
                ProfileStatus = _localization.Get("Profile.Resetting");
                var errors = await _tweaks.RevertAllAsync();
                ProfileStatus = errors == 0 ? _localization.Get("Restore.AllReverted")
                    : string.Format(CultureInfo.CurrentCulture, _localization.Get("Restore.RevertErrors"), errors);
                await RefreshCountsAsync();
            }
            finally
            {
                IsApplyingProfile = false;
            }

            return;
        }

        var lang = _localization.CurrentLanguage;
        var profile = ProfileCatalog.Get(kind, _tweaks.Tweaks);
        var todo = await Task.Run(() => profile.TweakIds
            .Select(_tweaks.Find)
            .OfType<TweakDefinition>()
            .Where(t => _tweaks.GetStatus(t) is TweakStatus.NotApplied or TweakStatus.Partial)
            .ToList());

        if (todo.Count == 0)
        {
            ProfileStatus = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.AllActive"), profileName);
            return;
        }

        var medium = _localization.Get("Profile.MediumMark");
        var list = string.Join("\n", todo.Select(t => "• " + t.Title.Get(lang) + (t.Risk == TweakRisk.Medium ? $" ({medium})" : string.Empty)));
        var question = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Confirm"), profileName, todo.Count) + "\n\n" + list;
        if (todo.Any(t => t.Risk == TweakRisk.Medium))
        {
            question += "\n\n" + _localization.Get("Profile.MediumHint");
        }

        if (MessageBox.Show(question, appName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsApplyingProfile = true;
        try
        {
            int ok = 0, failed = 0;
            for (var i = 0; i < todo.Count; i++)
            {
                var tweak = todo[i];
                var progress = $"{tweak.Title.Get(lang)} ({i + 1}/{todo.Count})";
                ProfileStatus = progress;
                var prompt = new DialogSafetyPrompt(_localization, stage => ProfileStatus = $"{progress}: {stage}");
                var result = await _tweaks.ApplyAsync(tweak, prompt);
                if (result.Success)
                {
                    ok++;
                }
                else
                {
                    failed++;
                    if (result.Error == "Abgebrochen.")
                    {
                        break;
                    }
                }
            }

            _history.Add(HistoryAction.ProfileApplied, null, failed == 0, $"{kind}: {ok} ok, {failed} Fehler");
            ProfileStatus = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Done"), profileName, ok, failed);
            await RefreshCountsAsync();
        }
        finally
        {
            IsApplyingProfile = false;
        }
    }

    private async Task RefreshCountsAsync() => ActiveTweaks = await Task.Run(_tweaks.CountApplied);

    /// <summary>Datum des neuesten Wiederherstellungspunkts, leer = keiner vorhanden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestorePoint))]
    private string _lastRestorePoint = string.Empty;

    public bool HasRestorePoint => LastRestorePoint.Length > 0;

    [ObservableProperty]
    private int _activeTweaks;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _operatingSystem = string.Empty;

    [ObservableProperty]
    private string _processor = string.Empty;

    [ObservableProperty]
    private string _graphics = string.Empty;

    [ObservableProperty]
    private string _memory = string.Empty;

    [ObservableProperty]
    private bool _isAdministrator;

    [ObservableProperty]
    private bool _isUnsupported;

    public override async Task OnNavigatedToAsync()
    {
        // Zähler bei jedem Öffnen aktualisieren, Systemdaten nur einmal lesen
        var (count, last) = await Task.Run(() => (_tweaks.CountApplied(), _restorePoints.List().FirstOrDefault()));
        ActiveTweaks = count;
        LastRestorePoint = last is null ? string.Empty : last.Created.ToString("g", CultureInfo.CurrentUICulture);

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var info = await Task.Run(_provider.Read);
            OperatingSystem = info.OperatingSystem;
            Processor = info.Processor;
            Graphics = info.GraphicsCards.Count > 0 ? string.Join(Environment.NewLine, info.GraphicsCards) : "?";
            Memory = info.MemoryGb > 0 ? info.MemoryGb.ToString("0.#", CultureInfo.CurrentCulture) + " GB" : "?";
            IsAdministrator = info.IsAdministrator;
            IsUnsupported = !info.IsSupported;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Systemdaten konnten nicht gelesen werden", ex);
            OperatingSystem = Processor = Graphics = Memory = "?";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
