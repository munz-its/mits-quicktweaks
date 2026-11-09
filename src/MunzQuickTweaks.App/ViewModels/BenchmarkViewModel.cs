using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.App.ViewModels;

/// <summary>Eine Messgröße mit Vergleich zum vorherigen Durchlauf.</summary>
public sealed record BenchmarkRow(string Label, string Value, string Change, bool IsBetter, bool IsWorse);

public sealed record BenchmarkHistoryRow(string Time, string Summary, string Note);

public sealed partial class DriveChoice : ObservableObject
{
    public DriveChoice(string name, bool selected)
    {
        Name = name;
        _isSelected = selected;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Benchmark für Prozessor, Arbeitsspeicher, Datenträger und Grafik mit Vorher-Nachher-Vergleich.</summary>
public sealed partial class BenchmarkViewModel : PageViewModel
{
    private readonly BenchmarkStore _store;
    private readonly IGpuBenchmark _gpu;
    private readonly GameModeController _gameMode;
    private readonly UpdateService _app;
    private readonly LocalizationService _localization;
    private readonly ILogger<BenchmarkViewModel> _log;
    private CancellationTokenSource? _cancel;

    public BenchmarkViewModel(BenchmarkStore store, IGpuBenchmark gpu, GameModeController gameMode, UpdateService app,
        LocalizationService localization, ILogger<BenchmarkViewModel> log)
        : base("Nav.Benchmark")
    {
        _store = store;
        _gpu = gpu;
        _gameMode = gameMode;
        _app = app;
        _localization = localization;
        _log = log;
        localization.LanguageChanged += (_, _) => ShowLatest();
    }

    public ObservableCollection<DriveChoice> Drives { get; } = new();

    public ObservableCollection<BenchmarkRow> Rows { get; } = new();

    public ObservableCollection<BenchmarkHistoryRow> History { get; } = new();

    public bool HasResult => Rows.Count > 0;

    public bool HasHistory => History.Count > 0;

    public string? SelectedDrive => Drives.FirstOrDefault(d => d.IsSelected)?.Name;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _runCpu = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _runRam = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _runDisk = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _runGpu = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelCommand))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty]
    private string _progress = string.Empty;

    [ObservableProperty]
    private string _compareText = string.Empty;

    public override Task OnNavigatedToAsync()
    {
        if (!IsRunning)
        {
            var previous = SelectedDrive ?? Path.GetPathRoot(Environment.SystemDirectory);
            Drives.Clear();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                Drives.Add(new DriveChoice(drive.Name, string.Equals(drive.Name, previous, StringComparison.OrdinalIgnoreCase)));
            }

            if (SelectedDrive is null && Drives.Count > 0)
            {
                Drives[0].IsSelected = true;
            }
            ShowLatest();
        }

        return Task.CompletedTask;
    }

    private bool CanStart() => !IsRunning && (RunCpu || RunRam || RunDisk || RunGpu);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        var result = new BenchmarkResult
        {
            Time = DateTimeOffset.UtcNow,
            AppVersion = _app.CurrentVersion,
            Note = _gameMode.IsActive ? _localization.Get("Bench.NoteGameMode") : null,
        };
        var errors = new List<string>();

        try
        {
            if (RunCpu)
            {
                Progress = _localization.Get("Bench.RunningCpu");
                result.CpuSingle = await Task.Run(() => Benchmarks.CpuSingle(TimeSpan.FromSeconds(4), cancel), cancel);
                result.CpuMulti = await Task.Run(() => Benchmarks.CpuMulti(TimeSpan.FromSeconds(4), cancel), cancel);
            }

            if (RunRam)
            {
                Progress = _localization.Get("Bench.RunningRam");
                await Measure(() => result.RamGBs = Benchmarks.RamCopy(TimeSpan.FromSeconds(3), cancel), "RAM", errors, cancel);
            }

            if (RunDisk && SelectedDrive is { } drive)
            {
                Progress = string.Format(CultureInfo.CurrentCulture, _localization.Get("Bench.RunningDisk"), drive);
                result.Drive = drive;
                await Measure(() =>
                {
                    var folder = string.Equals(drive, Path.GetPathRoot(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                        ? Path.GetTempPath()
                        : drive;
                    var (write, read, iops) = Benchmarks.Disk(folder, 1L << 30, TimeSpan.FromSeconds(3), cancel);
                    result.DiskWriteMBs = write;
                    result.DiskReadMBs = read;
                    result.DiskIops = iops;
                }, "SSD", errors, cancel);
            }

            if (RunGpu)
            {
                Progress = _localization.Get("Bench.RunningGpu");
                await Measure(() =>
                {
                    var (name, fps) = _gpu.Run(TimeSpan.FromSeconds(6), cancel);
                    result.GpuName = name;
                    result.GpuFps = fps;
                }, "GPU", errors, cancel);
            }

            _store.Add(result);
            _log.LogInformation("Benchmark: CPU {Single}/{Multi}, RAM {Ram}, SSD {Write}/{Read}/{Iops}, GPU {Fps} ({Gpu})",
                result.CpuSingle, result.CpuMulti, result.RamGBs, result.DiskWriteMBs, result.DiskReadMBs, result.DiskIops, result.GpuFps, result.GpuName);
            Progress = errors.Count == 0
                ? _localization.Get("Bench.Done")
                : $"{_localization.Get("Bench.DoneWithErrors")} {string.Join(" · ", errors)}";
        }
        catch (OperationCanceledException)
        {
            Progress = _localization.Get("Bench.Cancelled");
        }
        finally
        {
            IsRunning = false;
            _cancel.Dispose();
            _cancel = null;
        }

        ShowLatest();
    }

    /// <summary>Einzelner Test: Fehler stoppen nicht den ganzen Durchlauf.</summary>
    private async Task Measure(Action test, string name, List<string> errors, CancellationToken cancel)
    {
        try
        {
            await Task.Run(test, cancel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Benchmark {Name} fehlgeschlagen", name);
            errors.Add($"{name}: {ex.Message}");
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancel?.Cancel();

    private void ShowLatest()
    {
        var all = _store.All();
        Rows.Clear();
        History.Clear();
        var latest = all.FirstOrDefault();
        var previous = all.Skip(1).FirstOrDefault();
        if (latest is not null)
        {
            Add("Bench.CpuSingle", latest.CpuSingle, previous?.CpuSingle, "0", _localization.Get("Bench.Points"));
            Add("Bench.CpuMulti", latest.CpuMulti, previous?.CpuMulti, "0", _localization.Get("Bench.Points"));
            Add("Bench.Ram", latest.RamGBs, previous?.RamGBs, "0.0", "GB/s");
            Add("Bench.DiskWrite", latest.DiskWriteMBs, previous?.DiskWriteMBs, "0", "MB/s");
            Add("Bench.DiskRead", latest.DiskReadMBs, previous?.DiskReadMBs, "0", "MB/s");
            Add("Bench.DiskIops", latest.DiskIops, previous?.DiskIops, "0", "IOPS");
            Add("Bench.Gpu", latest.GpuFps, previous?.GpuFps, "0.0", "FPS");
        }

        CompareText = latest is null
            ? string.Empty
            : previous is null
                ? _localization.Get("Bench.FirstRun")
                : string.Format(CultureInfo.CurrentCulture, _localization.Get("Bench.ComparedTo"), previous.Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

        foreach (var run in all.Take(10))
        {
            var parts = new List<string>();
            if (run.CpuMulti is { } cpu) parts.Add($"CPU {cpu.ToString("0", CultureInfo.CurrentCulture)}");
            if (run.RamGBs is { } ram) parts.Add($"RAM {ram.ToString("0.0", CultureInfo.CurrentCulture)} GB/s");
            if (run.DiskReadMBs is { } disk) parts.Add($"SSD {disk.ToString("0", CultureInfo.CurrentCulture)} MB/s");
            if (run.GpuFps is { } gpu) parts.Add($"GPU {gpu.ToString("0.0", CultureInfo.CurrentCulture)} FPS");
            History.Add(new BenchmarkHistoryRow(run.Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), string.Join(" · ", parts), run.Note ?? string.Empty));
        }

        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasHistory));
    }

    private void Add(string key, double? now, double? before, string format, string unit)
    {
        if (now is null)
        {
            return;
        }

        var change = Benchmarks.Change(now, before);
        var changeText = change is { } c ? (c > 0 ? "+" : string.Empty) + c.ToString("0.0", CultureInfo.CurrentCulture) + " %" : string.Empty;

        // Unter 3 % gilt als Messschwankung, nicht als besser oder schlechter
        Rows.Add(new BenchmarkRow(_localization.Get(key), $"{now.Value.ToString(format, CultureInfo.CurrentCulture)} {unit}", changeText,
            change is > 3, change is < -3));
    }
}
