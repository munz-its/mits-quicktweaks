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

    public override string HeadingKey => "Dashboard.Heading";

    /// <summary>Wird vom Hauptfenster gesetzt, damit Links auf andere Seiten führen können.</summary>
    public Action<string>? NavigateRequested { get; set; }

    public string Version { get; } = "Version " + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?");

    public System.Collections.ObjectModel.ObservableCollection<HistoryRow> Recent { get; } = new();

    [ObservableProperty]
    private string _availableText = string.Empty;

    [ObservableProperty]
    private string _protectionText = string.Empty;

    [ObservableProperty]
    private bool _isProtectionOn;

    [ObservableProperty]
    private string _restoreSub = string.Empty;

    [ObservableProperty]
    private string _recommendedInfo = string.Empty;

    [ObservableProperty]
    private string _gamingInfo = string.Empty;

    [ObservableProperty]
    private string _privacyInfo = string.Empty;

    [ObservableProperty]
    private string _resetInfo = string.Empty;

    [RelayCommand]
    private void OpenHistory() => NavigateRequested?.Invoke("Nav.Restore");

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

    /// <summary>Kacheln, Profil-Zähler und "Zuletzt geändert" neu berechnen.</summary>
    private async Task RefreshCountsAsync()
    {
        var data = await Task.Run(() =>
        {
            var statuses = _tweaks.Tweaks.ToDictionary(t => t.Id, t => _tweaks.GetStatus(t));
            var points = _restorePoints.List();
            return (
                Statuses: statuses,
                Protection: _restorePoints.GetProtectionState(),
                Last: points.FirstOrDefault(),
                History: _history.Latest(3));
        });

        var available = data.Statuses.Values.Count(s => s != TweakStatus.NotAvailable);
        ActiveTweaks = data.Statuses.Values.Count(s => s == TweakStatus.Applied);
        AvailableText = string.Format(CultureInfo.CurrentCulture, _localization.Get("Dashboard.OfAvailable"), available);

        IsProtectionOn = data.Protection == ProtectionState.Enabled;
        ProtectionText = _localization.Get($"Dashboard.Protection.{data.Protection}");

        if (data.Last is null)
        {
            LastRestorePoint = string.Empty;
            RestoreSub = _localization.Get("Dashboard.RestoreHint");
        }
        else
        {
            LastRestorePoint = data.Last.Created.Date == DateTime.Today
                ? _localization.Get("Dashboard.Today")
                : data.Last.Created.ToString("d", CultureInfo.CurrentUICulture);
            RestoreSub = data.Last.Created.ToString("g", CultureInfo.CurrentUICulture);
        }

        string Info(ProfileKind kind)
        {
            var ids = ProfileCatalog.Get(kind, _tweaks.Tweaks).TweakIds
                .Where(id => data.Statuses.TryGetValue(id, out var s) && s != TweakStatus.NotAvailable)
                .ToList();
            var medium = ids.Count(id => _tweaks.Find(id)?.Risk == TweakRisk.Medium);
            return medium == 0
                ? string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Count"), ids.Count)
                : string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.CountMedium"), ids.Count, medium);
        }

        RecommendedInfo = Info(ProfileKind.Recommended);
        GamingInfo = Info(ProfileKind.Gaming);
        PrivacyInfo = Info(ProfileKind.Privacy);
        ResetInfo = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.ActiveCount"), ActiveTweaks);

        var lang = _localization.CurrentLanguage;
        Recent.Clear();
        foreach (var item in data.History)
        {
            var subject = item.TweakId is null ? string.Empty : ": " + (_tweaks.Find(item.TweakId)?.Title.Get(lang) ?? item.TweakId);
            Recent.Add(new HistoryRow(item.Time.ToLocalTime().ToString("t", CultureInfo.CurrentUICulture),
                _localization.Get($"History.{item.Action}") + subject, item.Success));
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    public bool HasRecent => Recent.Count > 0;

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
        await RefreshCountsAsync();

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
