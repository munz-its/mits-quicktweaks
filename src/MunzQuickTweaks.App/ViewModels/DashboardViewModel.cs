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

    private readonly BatchApplyService _batch;

    public DashboardViewModel(SystemInfoProvider provider, TweakService tweaks, IRestorePointService restorePoints, LocalizationService localization, HistoryStore history, BatchApplyService batch)
        : base("Nav.Dashboard")
    {
        _batch = batch;
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
    [NotifyCanExecuteChangedFor(nameof(ApplyProfileCommand), nameof(ReapplyResetCommand))]
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

        var profile = ProfileCatalog.Get(kind, _tweaks.Tweaks);
        var todo = await _batch.FilterTodoAsync(profile.TweakIds);
        if (todo.Count == 0)
        {
            ProfileStatus = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.AllActive"), profileName);
            return;
        }

        IsApplyingProfile = true;
        try
        {
            var heading = string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Confirm"), profileName, todo.Count);
            var result = await _batch.ConfirmAndApplyAsync(todo, heading, text => ProfileStatus = text, kind.ToString());
            ProfileStatus = result.Cancelled
                ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, _localization.Get("Profile.Done"), profileName, result.Applied, result.Failed);
            await RefreshCountsAsync();
        }
        finally
        {
            IsApplyingProfile = false;
        }
    }

    /// <summary>Tweaks, die ein Windows-Update zurückgesetzt hat, wieder anwenden.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyProfile))]
    private async Task ReapplyResetAsync()
    {
        var todo = await Task.Run(_tweaks.FindResetByWindows);
        if (todo.Count == 0)
        {
            await RefreshCountsAsync();
            return;
        }

        IsApplyingProfile = true;
        try
        {
            var heading = string.Format(CultureInfo.CurrentCulture, _localization.Get("Reset.Confirm"), todo.Count);
            var result = await _batch.ConfirmAndApplyAsync(todo, heading, text => ProfileStatus = text, "Reapply");
            ProfileStatus = result.Cancelled
                ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, _localization.Get("Reset.Done"), result.Applied, result.Failed);
            await RefreshCountsAsync();
        }
        finally
        {
            IsApplyingProfile = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResetTweaks))]
    private string _resetText = string.Empty;

    public bool HasResetTweaks => ResetText.Length > 0;

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
                History: _history.Latest(3),
                Reset: _tweaks.FindResetByWindows().Count);
        });

        ResetText = data.Reset == 0 ? string.Empty : string.Format(CultureInfo.CurrentCulture, _localization.Get("Reset.Banner"), data.Reset);

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
