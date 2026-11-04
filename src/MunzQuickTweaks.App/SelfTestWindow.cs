using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tweaks;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App;

/// <summary>
/// Versteckter Testmodus, Start mit "MunzQuickTweaks.exe --selftest".
/// Wendet jeden Tweak an, prüft ihn, setzt ihn zurück und legt einen Bericht auf den Desktop.
/// Gedacht für Test-VMs.
/// </summary>
public sealed class SelfTestWindow : Window
{
    private readonly TextBlock _status = new();
    private readonly TextBlock _detail = new();
    private readonly Border _bar = new();
    private readonly Border _track = new();

    private readonly TweakService _tweaks;
    private readonly IRegistry _registry;
    private readonly ISystemNotifier _notifier;
    private readonly IPowerPlans _power;
    private readonly IRestorePointService _restorePoints;
    private readonly SystemInfoProvider _systemInfo;
    private readonly ILogger<SelfTestWindow> _log;

    public SelfTestWindow(TweakService tweaks, IRegistry registry, ISystemNotifier notifier, IPowerPlans power,
        IRestorePointService restorePoints, SystemInfoProvider systemInfo, ILogger<SelfTestWindow> log)
    {
        _tweaks = tweaks;
        _registry = registry;
        _notifier = notifier;
        _power = power;
        _restorePoints = restorePoints;
        _systemInfo = systemInfo;
        _log = log;

        Title = "Munz Quick Tweaks · Selbsttest";
        Width = 560;
        Height = 240;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("Bg");

        var title = new TextBlock { Text = "Selbsttest", Style = (Style)FindResource("H2"), Margin = new Thickness(0, 0, 0, 12) };
        _status.Style = (Style)FindResource("TextBase");
        _detail.Style = (Style)FindResource("MutedText");
        _detail.Margin = new Thickness(0, 6, 0, 0);

        _track.Height = 8;
        _track.CornerRadius = new CornerRadius(4);
        _track.Background = (Brush)FindResource("Bg3");
        _track.Margin = new Thickness(0, 18, 0, 0);
        _bar.HorizontalAlignment = HorizontalAlignment.Left;
        _bar.CornerRadius = new CornerRadius(4);
        _bar.Background = (Brush)FindResource("Blue");
        _bar.Width = 0;
        _track.Child = _bar;

        Content = new StackPanel { Margin = new Thickness(28, 24, 28, 24), Children = { title, _status, _detail, _track } };
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var confirm = MessageBox.Show(
            "Der Selbsttest wendet jeden Tweak einmal an und setzt ihn sofort wieder zurück.\n\n" +
            "Gedacht für eine Test-VM. Tweaks, die du selbst aktiv hast, werden übersprungen.\n" +
            "Vorher wird ein Wiederherstellungspunkt erstellt.\n\nStarten?",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            Close();
            return;
        }

        try
        {
            _status.Text = "Wiederherstellungspunkt wird erstellt …";
            string pointNote;
            try
            {
                var point = await Task.Run(() => _restorePoints.Create("Munz Quick Tweaks: vor Selbsttest"));
                pointNote = point.Success ? "Wiederherstellungspunkt: erstellt" : $"Wiederherstellungspunkt: fehlgeschlagen ({point.Error})";
            }
            catch (Exception ex)
            {
                // In frischen VMs ist der Systemschutz oft aus: Test läuft trotzdem weiter
                _log.LogWarning(ex, "Selbsttest: Wiederherstellungspunkt nicht möglich");
                pointNote = $"Wiederherstellungspunkt: nicht möglich ({ex.GetType().Name}: {ex.Message})";
            }

            // Eigenes Journal: der Test berührt das Journal des Nutzers nie
            var testJournal = new JournalStore(Path.Combine(AppPaths.MachineData, "selftest", $"journal-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
            var engine = new TweakEngine(_registry, testJournal, _notifier, _systemInfo.ReadBuild(), _power)
            {
                UserHiveAllowed = _tweaks.Engine.UserHiveAllowed,
            };
            var runner = new SelfTestRunner(engine, _tweaks.Engine.Journal);

            var total = _tweaks.Tweaks.Count;
            var progress = new Progress<(int Done, int Total, string Id)>(p =>
            {
                _status.Text = p.Done < p.Total ? $"Tweak {p.Done + 1} von {p.Total}" : "Bericht wird erstellt …";
                _detail.Text = p.Id;
                _bar.Width = _track.ActualWidth * p.Done / Math.Max(1, p.Total);
            });

            var results = await Task.Run(() => runner.Run(_tweaks.Tweaks, progress));

            string system;
            var build = _systemInfo.ReadBuild();
            try
            {
                var info = await Task.Run(_systemInfo.Read);
                system = $"{info.OperatingSystem} · {info.Processor} · {(info.IsAdministrator ? "Administrator" : "kein Administrator")}\n{pointNote}";
            }
            catch (Exception ex)
            {
                system = $"Windows-Build {build} (Systemdaten nicht lesbar: {ex.Message})\n{pointNote}";
            }
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
            var report = SelfTestRunner.FormatReport(results, system, version, DateTime.Now);

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var file = Path.Combine(desktop, $"MunzQuickTweaks-Selbsttest-Build{build}-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            await File.WriteAllTextAsync(file, report);
            _log.LogInformation("Selbsttest fertig: {File}", file);

            var failed = results.Count(r => r.Outcome == SelfTestOutcome.Failed);
            _status.Text = failed == 0 ? $"Fertig ✓ Alle getesteten Tweaks sind in Ordnung." : $"Fertig: {failed} von {total} Tweaks mit Fehlern.";
            _detail.Text = $"Bericht: {file}";
            _bar.Width = _track.ActualWidth;

            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { file }, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Selbsttest abgebrochen");
            _status.Text = "Selbsttest abgebrochen.";
            _detail.Text = $"{ex.GetType().Name}: {ex.Message}";
            _detail.TextWrapping = TextWrapping.Wrap;
            Height = 320;

            // Fehler zusätzlich als Datei auf den Desktop, damit er sich kopieren lässt
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                File.WriteAllText(Path.Combine(desktop, "MunzQuickTweaks-Selbsttest-Fehler.txt"), ex.ToString());
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Ohne Desktop-Zugriff bleibt der Fehler im Protokoll
            }
        }
    }
}
