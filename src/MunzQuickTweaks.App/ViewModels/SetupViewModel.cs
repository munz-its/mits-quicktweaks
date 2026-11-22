using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Ein Schritt im Assistenten, für die Leiste oben.</summary>
public sealed partial class SetupStep : ObservableObject
{
    public SetupStep(int index)
    {
        Index = index;
        Number = (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    public int Index { get; }

    public string Number { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isDone;
}

/// <summary>Auswahl einer Voreinstellung im Assistenten.</summary>
public sealed partial class SetupProfileChoice : ObservableObject
{
    public SetupProfileChoice(ProfileKind kind)
    {
        Kind = kind;
    }

    public ProfileKind Kind { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Neuer-PC-Assistent: Benchmark vorher, Voreinstellung, vorinstallierte Apps, Programme, Benchmark nachher mit Zusammenfassung.
/// Nutzt die vorhandenen Bausteine (Benchmark, Profile mit Vorschau, Installer, PC-Bericht). Jeder Schritt lässt sich überspringen.
/// </summary>
public sealed partial class SetupViewModel : PageViewModel
{
    public const int StepCount = 5;

    /// <summary>Werbe-Apps und Microsoft-Extras. Xbox bleibt, viele Spiele brauchen die Xbox-Apps.</summary>
    private static readonly DebloatGroup[] DebloatGroups = { DebloatGroup.Ads, DebloatGroup.MicrosoftExtras };

    private readonly ProfileService _profiles;
    private readonly ReportService _reports;
    private readonly LocalizationService _localization;
    private readonly ILogger<SetupViewModel> _log;
    private bool _benchBeforeDone;

    public SetupViewModel(BenchmarkViewModel benchmark, InstallerViewModel installer, PresetsViewModel presets, ProfileService profiles,
        ReportService reports, LocalizationService localization, ILogger<SetupViewModel> log)
        : base("Nav.Setup")
    {
        Benchmark = benchmark;
        Installer = installer;
        Presets = presets;
        _profiles = profiles;
        _reports = reports;
        _localization = localization;
        _log = log;

        Steps = new ObservableCollection<SetupStep>(Enumerable.Range(0, StepCount).Select(i => new SetupStep(i)));
        Profiles = new ObservableCollection<SetupProfileChoice>(new[]
        {
            ProfileKind.Recommended, ProfileKind.Gaming, ProfileKind.MaxPerformance, ProfileKind.Privacy, ProfileKind.Battery, ProfileKind.Office,
        }.Select(k => new SetupProfileChoice(k)));
        Profiles[0].IsSelected = true;
        RefreshTexts();
        localization.LanguageChanged += (_, _) => RefreshTexts();
        UpdateSteps();

        // Weiter und Zurück sperren, solange ein Benchmark oder eine Installation läuft
        benchmark.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BenchmarkViewModel.IsRunning))
            {
                BackCommand.NotifyCanExecuteChanged();
                NextCommand.NotifyCanExecuteChanged();
            }
        };
        installer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstallerViewModel.IsBusy))
            {
                BackCommand.NotifyCanExecuteChanged();
                NextCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public BenchmarkViewModel Benchmark { get; }

    public InstallerViewModel Installer { get; }

    public PresetsViewModel Presets { get; }

    public ObservableCollection<SetupStep> Steps { get; }

    public ObservableCollection<SetupProfileChoice> Profiles { get; }

    public ObservableCollection<string> Summary { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsFirst), nameof(IsLast), nameof(StepText))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    private int _stepIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand), nameof(ApplyProfileCommand), nameof(RemoveAppsCommand), nameof(CreateReportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _profileStatus = string.Empty;

    [ObservableProperty]
    private string _appsStatus = string.Empty;

    [ObservableProperty]
    private string _reportStatus = string.Empty;

    public bool IsStep0 => StepIndex == 0;

    public bool IsStep1 => StepIndex == 1;

    public bool IsStep2 => StepIndex == 2;

    public bool IsStep3 => StepIndex == 3;

    public bool IsStep4 => StepIndex == 4;

    public bool IsFirst => StepIndex == 0;

    public bool IsLast => StepIndex == StepCount - 1;

    public string StepText => string.Format(CultureInfo.CurrentCulture, _localization.Get("Setup.StepOf"), StepIndex + 1, StepCount);

    public override Task OnNavigatedToAsync() => EnterStepAsync();

    private void RefreshTexts()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Title = _localization.Get($"Setup.Step{i}");
        }

        foreach (var choice in Profiles)
        {
            choice.Name = _localization.Get($"Profile.{choice.Kind}");
            choice.Hint = _localization.Get($"Profile.{choice.Kind}.Hint");
        }

        OnPropertyChanged(nameof(StepText));
    }

    private void UpdateSteps()
    {
        foreach (var step in Steps)
        {
            step.IsCurrent = step.Index == StepIndex;
        }
    }

    partial void OnStepIndexChanged(int value) => UpdateSteps();

    /// <summary>Daten des neuen Schritts vorbereiten: Laufwerke für den Benchmark, Programmliste für den Installer.</summary>
    private async Task EnterStepAsync()
    {
        switch (StepIndex)
        {
            case 0:
            case 4:
                await Benchmark.OnNavigatedToAsync();
                break;
            case 3:
                await Installer.PrepareAsync();
                break;
        }

        if (IsLast)
        {
            BuildSummary();
        }
    }

    private bool CanGo() => !IsBusy && !Benchmark.IsRunning && !Installer.IsBusy;

    private bool CanBack() => CanGo() && StepIndex > 0;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private async Task BackAsync()
    {
        if (StepIndex > 0)
        {
            StepIndex--;
            await EnterStepAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGo))]
    private async Task NextAsync()
    {
        if (StepIndex == 0 && Benchmark.HasResult)
        {
            _benchBeforeDone = true;
        }

        Steps[StepIndex].IsDone = true;
        if (StepIndex < StepCount - 1)
        {
            StepIndex++;
            await EnterStepAsync();
        }
    }

    [RelayCommand]
    private async Task GoToAsync(SetupStep step)
    {
        if (!CanGo() || step.Index == StepIndex)
        {
            return;
        }

        StepIndex = step.Index;
        await EnterStepAsync();
    }

    /// <summary>Benchmark vorher oder nachher, über die Benchmark-Seite, damit der Vergleich dort erhalten bleibt.</summary>
    [RelayCommand]
    private async Task RunBenchmarkAsync()
    {
        if (Benchmark.StartCommand.CanExecute(null))
        {
            await Benchmark.StartCommand.ExecuteAsync(null);
            if (StepIndex == 0)
            {
                _benchBeforeDone = Benchmark.HasResult;
            }

            if (IsLast)
            {
                BuildSummary();
            }
        }
    }

    [RelayCommand]
    private void SelectProfile(SetupProfileChoice choice)
    {
        foreach (var item in Profiles)
        {
            item.IsSelected = ReferenceEquals(item, choice);
        }
    }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ApplyProfileAsync()
    {
        var choice = Profiles.FirstOrDefault(p => p.IsSelected);
        if (choice is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ProfileStatus = await _profiles.RunProfileAsync(choice.Kind, text => ProfileStatus = text);
            _log.LogInformation("Assistent: Profil {Kind}", choice.Kind);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RemoveAppsAsync()
    {
        IsBusy = true;
        try
        {
            var profile = new TweakProfile(ProfileKind.Custom, Array.Empty<string>()) { Debloat = DebloatGroups };
            AppsStatus = await _profiles.RunImportedAsync(profile, _localization.Get("Setup.AppsName"), _localization.Get("Setup.AppsIntro"),
                text => AppsStatus = text);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CreateReportAsync()
    {
        IsBusy = true;
        ReportStatus = _localization.Get("Report.Creating");
        try
        {
            var path = await _reports.CreateAndOpenAsync();
            ReportStatus = string.Format(CultureInfo.CurrentCulture, _localization.Get("Report.Saved"), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportStatus = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        foreach (var step in Steps)
        {
            step.IsDone = false;
        }

        ProfileStatus = AppsStatus = ReportStatus = string.Empty;
        _benchBeforeDone = false;
        StepIndex = 0;
        await EnterStepAsync();
    }

    /// <summary>Was in diesem Durchlauf passiert ist, in einfachen Sätzen.</summary>
    private void BuildSummary()
    {
        Summary.Clear();
        Summary.Add(_localization.Get(_benchBeforeDone ? "Setup.SumBenchBefore" : "Setup.SumBenchSkipped"));
        Summary.Add(ProfileStatus.Length > 0 ? ProfileStatus : _localization.Get("Setup.SumProfileSkipped"));
        Summary.Add(AppsStatus.Length > 0 ? AppsStatus : _localization.Get("Setup.SumAppsSkipped"));
        Summary.Add(Installer.Status.Length > 0 ? Installer.Status : _localization.Get("Setup.SumProgramsSkipped"));
        if (_benchBeforeDone && Benchmark.HasResult && Benchmark.CompareText.Length > 0)
        {
            Summary.Add(Benchmark.CompareText);
        }
    }
}
