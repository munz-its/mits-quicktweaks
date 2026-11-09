using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.App.ViewModels;

public sealed partial class StartupItemViewModel : ObservableObject
{
    private readonly StartupViewModel _owner;

    public StartupItemViewModel(StartupEntry entry, string sourceText, StartupViewModel owner)
    {
        Entry = entry;
        SourceText = sourceText;
        _owner = owner;
        _isEnabled = entry.Enabled;
    }

    public StartupEntry Entry { get; set; }

    public string Name => Entry.Name;

    public string Path => StartupApproved.ExtractPath(Entry.Command);

    public string SourceText { get; }

    public string? Publisher => Entry.Publisher;

    public bool HasPublisher => Entry.Publisher is not null;

    public bool FileMissing => Entry.FileMissing;

    public bool CanRemove => Entry.CanRemove;

    [ObservableProperty]
    private bool _isEnabled;

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    [ObservableProperty]
    private string _message = string.Empty;

    [RelayCommand]
    private void Toggle() => _owner.Toggle(this);
}

/// <summary>Programme beim Windows-Start aktivieren oder deaktivieren, ohne sie zu löschen.</summary>
public sealed partial class StartupViewModel : PageViewModel
{
    private readonly IStartupManager _startup;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly TweakService _tweaks;
    private readonly ILogger<StartupViewModel> _log;

    public StartupViewModel(IStartupManager startup, HistoryStore history, LocalizationService localization, TweakService tweaks, ILogger<StartupViewModel> log)
        : base("Nav.Startup")
    {
        _startup = startup;
        _history = history;
        _localization = localization;
        _tweaks = tweaks;
        _log = log;
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    public bool IsForeignAccount => _tweaks.IsForeignAccount;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _summary = string.Empty;

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public override async Task OnNavigatedToAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await Task.Run(_startup.List);
            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(new StartupItemViewModel(entry, _localization.Get($"Startup.Source.{entry.Source}"), this));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Autostart nicht lesbar");
        }
        finally
        {
            IsLoading = false;
            UpdateSummary();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    internal void Toggle(StartupItemViewModel item)
    {
        // Der Schalter hat IsEnabled schon umgestellt: das ist der gewünschte neue Zustand
        var enable = item.IsEnabled;
        var result = _startup.SetEnabled(item.Entry, enable);
        _history.Add(enable ? HistoryAction.StartupEnabled : HistoryAction.StartupDisabled, null, result.Success,
            result.Success ? item.Name : $"{item.Name}: {result.Error}");

        if (result.Success)
        {
            item.Entry = item.Entry with { Enabled = enable };
            item.Message = string.Empty;
            _log.LogInformation("Autostart {Name}: {State}", item.Name, enable ? "an" : "aus");
        }
        else
        {
            item.IsEnabled = !enable;
            item.Message = $"{_localization.Get("Tweak.Failed")} {result.Error}";
        }

        UpdateSummary();
    }

    internal void Remove(StartupItemViewModel item)
    {
        var text = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Startup.ConfirmRemove"), item.Name);
        if (System.Windows.MessageBox.Show(text, _localization.Get("App.Name"), System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = _startup.Remove(item.Entry);
        _history.Add(HistoryAction.StartupRemoved, null, result.Success, result.Success ? item.Name : $"{item.Name}: {result.Error}");
        _log.LogInformation("Autostart {Name} gelöscht: {Success} {Error}", item.Name, result.Success, result.Error);
        if (result.Success)
        {
            Items.Remove(item);
            UpdateSummary();
            OnPropertyChanged(nameof(IsEmpty));
        }
        else
        {
            item.Message = $"{_localization.Get("Tweak.Failed")} {result.Error}";
        }
    }

    private void UpdateSummary() =>
        Summary = string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Startup.Summary"),
            Items.Count, Items.Count(i => i.IsEnabled));
}
