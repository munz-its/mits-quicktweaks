using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.App.ViewModels;

public sealed record ProcessRuleRow(string Exe, string CpuText, string IoText);

/// <summary>Feste Priorität für Programme bei jedem Start, z. B. Spiele höher, Updater niedriger.</summary>
public sealed partial class ProcessRulesViewModel : PageViewModel
{
    private readonly ProcessRules _rules;
    private readonly IRegistryBackup _backup;
    private readonly HistoryStore _history;
    private readonly LocalizationService _localization;
    private readonly ILogger<ProcessRulesViewModel> _log;

    public ProcessRulesViewModel(IRegistry registry, IRegistryBackup backup, HistoryStore history, LocalizationService localization, ChangeLedger ledger, ILogger<ProcessRulesViewModel> log)
        : base("Nav.Processes")
    {
        _rules = new ProcessRules(registry);
        _ledger = ledger;
        _backup = backup;
        _history = history;
        _localization = localization;
        _log = log;
    }

    private readonly ChangeLedger _ledger;

    public ObservableCollection<ProcessRuleRow> Rules { get; } = new();

    public bool HasRules => Rules.Count > 0;

    [ObservableProperty]
    private string _exeName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsBelowNormal), nameof(IsAboveNormal), nameof(IsHigh))]
    private CpuPriority _cpu = CpuPriority.AboveNormal;

    public bool IsIdle { get => Cpu == CpuPriority.Idle; set { if (value) Cpu = CpuPriority.Idle; } }

    public bool IsBelowNormal { get => Cpu == CpuPriority.BelowNormal; set { if (value) Cpu = CpuPriority.BelowNormal; } }

    public bool IsAboveNormal { get => Cpu == CpuPriority.AboveNormal; set { if (value) Cpu = CpuPriority.AboveNormal; } }

    public bool IsHigh { get => Cpu == CpuPriority.High; set { if (value) Cpu = CpuPriority.High; } }

    [ObservableProperty]
    private bool _lowIo;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _statusIsError;

    public override Task OnNavigatedToAsync()
    {
        Status = string.Empty;
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var rules = await Task.Run(_rules.List);
            Rules.Clear();
            foreach (var rule in rules)
            {
                Rules.Add(new ProcessRuleRow(
                    rule.Exe,
                    rule.Cpu is { } cpu ? _localization.Get($"Process.Cpu.{cpu}") : "-",
                    rule.Io == IoPriority.Low ? _localization.Get("Process.IoLow") : string.Empty));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _log.LogError(ex, "Prozessregeln nicht lesbar");
            Show(true, $"{_localization.Get("Tweak.Failed")} {ex.Message}");
        }

        OnPropertyChanged(nameof(HasRules));
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programme (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true)
        {
            ExeName = Path.GetFileName(dialog.FileName);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var exe = ExeName.Trim();
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && exe.Length > 0)
        {
            exe += ".exe";
        }

        if (ProcessRules.Validate(exe) is { } error)
        {
            Show(true, error);
            return;
        }

        if (Cpu == CpuPriority.High
            && MessageBox.Show(_localization.Get("Process.ConfirmHigh"), _localization.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Backup(exe);
            _rules.Set(new ProcessRule(exe, Cpu, LowIo ? IoPriority.Low : null));
            _history.Add(HistoryAction.ProcessRuleSet, exe, true, $"{Cpu}{(LowIo ? ", IO niedrig" : string.Empty)}");
            _log.LogInformation("Prozessregel {Exe}: {Cpu} IO niedrig {Io}", exe, Cpu, LowIo);
            Show(false, string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Process.Saved"), exe));
            ExeName = string.Empty;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException)
        {
            _history.Add(HistoryAction.ProcessRuleSet, exe, false, ex.Message);
            Show(true, $"{_localization.Get("Tweak.Failed")} {ex.Message}");
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveAsync(ProcessRuleRow row)
    {
        try
        {
            Backup(row.Exe);
            _rules.Remove(row.Exe);
            _history.Add(HistoryAction.ProcessRuleRemoved, row.Exe, true, null);
            Show(false, string.Format(System.Globalization.CultureInfo.CurrentCulture, _localization.Get("Process.Removed"), row.Exe));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException)
        {
            _history.Add(HistoryAction.ProcessRuleRemoved, row.Exe, false, ex.Message);
            Show(true, $"{_localization.Get("Tweak.Failed")} {ex.Message}");
        }

        await LoadAsync();
    }

    /// <summary>Vorhandenen Schlüssel des Programms vorher als .reg sichern.</summary>
    private void Backup(string exe)
    {
        var original = _rules.Get(exe);
        _ledger.RecordProcess(exe, new ProcessOriginal((int?)original?.Cpu, (int?)original?.Io));

        var path = _backup.Export("process." + exe, new[] { (RegistryHive.HKLM, $@"{ProcessRules.BaseKey}\{exe}") });
        if (path is not null)
        {
            _history.Add(HistoryAction.RegistryExported, "process." + exe, true, path);
        }
    }

    private void Show(bool isError, string text)
    {
        StatusIsError = isError;
        Status = text;
    }
}
