using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

public sealed record DiskRow(DiskItem Item, string SizeText, double Share, string Detail)
{
    public string Name => Item.Name;

    public bool IsDirectory => Item.IsDirectory;

    /// <summary>Balkenbreite in Pixeln, bezogen auf den größten Eintrag.</summary>
    public double BarWidth => Math.Max(2, Share * 240);
}

public sealed record DriveRow(string Root, string Label, string Free);

/// <summary>Zeigt große Ordner und Dateien. Öffnet sie im Explorer, löscht aber selbst nichts.</summary>
public sealed partial class DiskViewModel : PageViewModel
{
    private readonly LocalizationService _localization;
    private readonly ILogger<DiskViewModel> _log;
    private readonly Stack<string> _back = new();
    private CancellationTokenSource? _cancel;

    public DiskViewModel(LocalizationService localization, ILogger<DiskViewModel> log)
        : base("Nav.Disk")
    {
        _localization = localization;
        _log = log;
    }

    public ObservableCollection<DriveRow> Drives { get; } = new();

    public ObservableCollection<DiskRow> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpCommand))]
    [NotifyPropertyChangedFor(nameof(PathText))]
    private string? _currentPath;

    public string PathText => CurrentPath ?? _localization.Get("Disk.PickDrive");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isScanning;

    public bool IsIdle => !IsScanning;

    [ObservableProperty]
    private string _status = string.Empty;

    public override Task OnNavigatedToAsync()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady))
        {
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} {drive.VolumeLabel}";
            var free = string.Format(CultureInfo.CurrentCulture, _localization.Get("Disk.Free"),
                DiskAnalyzer.FormatSize(drive.AvailableFreeSpace), DiskAnalyzer.FormatSize(drive.TotalSize));
            Drives.Add(new DriveRow(drive.Name, label, free));
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ScanDriveAsync(DriveRow drive)
    {
        _back.Clear();
        return ScanAsync(drive.Root);
    }

    [RelayCommand]
    private Task OpenAsync(DiskRow row)
    {
        if (!row.IsDirectory)
        {
            ShowInExplorer(row);
            return Task.CompletedTask;
        }

        if (CurrentPath is not null)
        {
            _back.Push(CurrentPath);
        }

        return ScanAsync(row.Item.Path);
    }

    private bool CanGoUp() => CurrentPath is not null && Directory.GetParent(CurrentPath) is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task UpAsync()
    {
        var parent = _back.Count > 0 ? _back.Pop() : Directory.GetParent(CurrentPath!)!.FullName;
        return ScanAsync(parent);
    }

    [RelayCommand]
    private static void ShowInExplorer(DiskRow row)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Item.Path}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Explorer nicht startbar: nichts zu tun
        }
    }

    [RelayCommand]
    private void OpenCurrentInExplorer()
    {
        if (CurrentPath is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CurrentPath}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Explorer nicht startbar: nichts zu tun
        }
    }

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    private async Task ScanAsync(string path)
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        IsScanning = true;
        CurrentPath = path;
        Items.Clear();
        OnPropertyChanged(nameof(HasItems));

        var progress = new Progress<string>(folder =>
            Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Disk.Scanning"), folder));
        try
        {
            var watch = Stopwatch.StartNew();
            var items = await Task.Run(() => DiskAnalyzer.Scan(path, progress, cancel), cancel);
            var largest = items.Count > 0 ? Math.Max(1, items[0].Size) : 1;
            var total = items.Sum(i => i.Size);
            foreach (var item in items.Take(200))
            {
                var detail = item.IsDirectory
                    ? string.Format(CultureInfo.CurrentCulture, _localization.Get("Disk.Files"), item.Files.ToString("N0", CultureInfo.CurrentCulture))
                    : _localization.Get("Disk.File");
                Items.Add(new DiskRow(item, DiskAnalyzer.FormatSize(item.Size), (double)item.Size / largest, detail));
            }

            Status = string.Format(CultureInfo.CurrentCulture, _localization.Get("Disk.Summary"),
                DiskAnalyzer.FormatSize(total), watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture));
            _log.LogInformation("Disk Analyzer {Path}: {Count} Einträge, {Total} Byte", path, items.Count, total);
        }
        catch (OperationCanceledException)
        {
            Status = _localization.Get("Disk.Cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"{_localization.Get("Tweak.Failed")} {ex.Message}";
        }
        finally
        {
            if (!cancel.IsCancellationRequested || _cancel?.Token == cancel)
            {
                IsScanning = false;
            }

            OnPropertyChanged(nameof(HasItems));
        }
    }
}
