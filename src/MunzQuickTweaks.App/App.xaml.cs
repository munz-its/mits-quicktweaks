using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.App.ViewModels;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Settings;
using MunzQuickTweaks.Core.Tweaks;
using MunzQuickTweaks.Core.Tools;
using MunzQuickTweaks.Windows;
using Serilog;

namespace MunzQuickTweaks.App;

public partial class App : Application
{
    // Nur eine Instanz gleichzeitig: zwei Instanzen könnten sich bei Tweaks gegenseitig überschreiben
    private const string MutexName = @"Global\MunzITService.MunzQuickTweaks";

    private Mutex? _singleInstance;
    private IHost? _host;

    public App()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        CrashLog.Initialize(version);

        // Läuft vor dem Laden von App.xaml: Fehler beim Start dürfen nie lautlos verschwinden
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = (Exception)args.ExceptionObject;
            CrashLog.Write("Unbehandelter Fehler", exception);
            CrashLog.Close();
            MessageBox.Show(
                $"Munz Quick Tweaks konnte nicht gestartet werden.\n\n{exception.GetBaseException().Message}\n\nDetails stehen in:\n{CrashLog.FilePath}",
                "Munz Quick Tweaks", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var isFirst);

        // Neustart nach Designwechsel: kurz warten, bis die alte Instanz beendet ist
        if (!isFirst && e.Args.Contains("--restart", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                isFirst = _singleInstance.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                isFirst = true;
            }
        }

        if (!isFirst)
        {
            MessageBox.Show("Munz Quick Tweaks läuft bereits.", "Munz Quick Tweaks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog(Log.Logger, dispose: false)
            .ConfigureServices(services =>
            {
                services.AddSingleton(new SettingsStore(AppPaths.SettingsFile));
                services.AddSingleton<SettingsService>();
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<SystemInfoProvider>();

                // Tweaks und Sicherung
                services.AddSingleton<IRegistry, WindowsRegistry>();
                services.AddSingleton<ISystemNotifier, WindowsNotifier>();
                services.AddSingleton<IRestorePointService, WindowsRestorePoints>();
                services.AddSingleton<IPowerPlans, WindowsPowerPlans>();
                services.AddSingleton<IPowerModes, WindowsPowerModes>();
                services.AddSingleton<IAppEnvironment, LocalAppEnvironment>();
                services.AddSingleton(new JournalStore(Path.Combine(AppPaths.MachineData, "state", "journal.json")));
                services.AddSingleton(new HistoryStore(Path.Combine(AppPaths.MachineData, "state", "history.jsonl")));
                services.AddSingleton<IRegistryBackup>(sp => new RegExeBackup(AppPaths.BackupDirectory, sp.GetRequiredService<IRegistry>()));
                services.AddSingleton<RestorePointGate>();
                services.AddSingleton(sp => new TweakEngine(
                    sp.GetRequiredService<IRegistry>(),
                    sp.GetRequiredService<JournalStore>(),
                    sp.GetRequiredService<ISystemNotifier>(),
                    sp.GetRequiredService<SystemInfoProvider>().ReadBuild(),
                    sp.GetRequiredService<IPowerPlans>(),
                    apps: sp.GetRequiredService<IAppEnvironment>())
                {
                    // Fremdes Admin-Konto: Benutzer-Tweaks würden im falschen Konto landen
                    UserHiveAllowed = !AccountInfo.IsForeignElevation(),
                });
                services.AddSingleton<TweakService>();
                services.AddSingleton<BatchApplyService>();
                services.AddSingleton<IAppxManager, WindowsAppx>();
                services.AddSingleton<IStartupManager>(_ => new WindowsStartup(includeUserLevel: !AccountInfo.IsForeignElevation(), AppPaths.BackupDirectory));
                services.AddSingleton<WindowsCleanup>();

                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<TweaksViewModel>();
                services.AddSingleton<PrivacyViewModel>();
                services.AddSingleton<AppsViewModel>();
                services.AddSingleton<ProcessRulesViewModel>();
                services.AddSingleton<PowerViewModel>();
                services.AddSingleton<BenchmarkViewModel>();
                services.AddSingleton<PresetsViewModel>();
                services.AddSingleton<NetworkViewModel>();
                services.AddSingleton<InstallerViewModel>();
                services.AddSingleton<HealthViewModel>();
                services.AddSingleton<ProfileService>();
                services.AddSingleton(new ChangeLedger(Path.Combine(AppPaths.StateDirectory, "ledger.json")));
                services.AddSingleton<DiskViewModel>();
                services.AddSingleton<DriversViewModel>();
                services.AddSingleton<GameModeViewModel>();
                services.AddSingleton<UpdateService>();
                services.AddSingleton<IGpuBenchmark, WindowsGpuBenchmark>();
                services.AddSingleton<IGameModeSystem, WindowsGameModeSystem>();
                services.AddSingleton(new BenchmarkStore(Path.Combine(AppPaths.StateDirectory, "benchmarks.jsonl")));
                services.AddSingleton(sp => new GameModeController(
                    sp.GetRequiredService<IGameModeSystem>(), sp.GetRequiredService<IRegistry>(), Path.Combine(AppPaths.StateDirectory, "gamemode.json")));
                services.AddSingleton<DebloatViewModel>();
                services.AddSingleton<StartupViewModel>();
                services.AddSingleton<CareViewModel>();
                services.AddSingleton<RestoreViewModel>();
                services.AddSingleton<HelpViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<SelfTestWindow>();
            })
            .Build();

        var settings = _host.Services.GetRequiredService<SettingsService>();
        Controls.UiDesign.Apply(this, settings.Current.Design);
        Controls.UiMotion.Enabled = settings.Current.AnimationsEnabled;
        Controls.GlassPanel.Enabled = settings.Current.GlassEnabled;
        _host.Services.GetRequiredService<LocalizationService>().Apply(settings.Current.Language);

        // Versteckter Testmodus für Test-VMs: MunzQuickTweaks.exe --selftest
        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var test = _host.Services.GetRequiredService<SelfTestWindow>();
            MainWindow = test;
            test.Show();
            return;
        }

        // Unsichtbare Prüfung nach dem Anmelden (geplante Aufgabe): MunzQuickTweaks.exe --check-reset
        if (e.Args.Any(a => string.Equals(a, MunzQuickTweaks.Windows.ResetCheckTask.Argument, StringComparison.OrdinalIgnoreCase)))
        {
            RunResetCheck();
            return;
        }

        EnsureResetTask(settings.Current.CheckResetAfterUpdates);
        ShowMain(reapply: false);
    }

    /// <summary>Geplante Aufgabe anlegen, Pfad aktualisieren oder entfernen. Fehler nur ins Protokoll.</summary>
    public static void EnsureResetTask(bool enabled)
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                MunzQuickTweaks.Windows.ResetCheckTask.Ensure(enabled, exe);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Aufgabe für die Update-Prüfung nicht eingerichtet");
        }
    }

    private void ShowMain(bool reapply)
    {
        var window = _host!.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        if (reapply)
        {
            // Nach dem Startbildschirm direkt die Rückfrage zum erneuten Anwenden zeigen
            var dashboard = _host.Services.GetRequiredService<DashboardViewModel>();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _ = dashboard.ReapplyResetCommand.ExecuteAsync(null);
            };
            timer.Start();
        }
    }

    private void RunResetCheck()
    {
        var tweaks = _host!.Services.GetRequiredService<TweakService>();
        var notice = new ResetNotice(Path.Combine(AppPaths.StateDirectory, "resetnotice.json"));
        List<string> ids;
        try
        {
            ids = tweaks.FindResetByWindows().Select(t => t.Id).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update-Prüfung fehlgeschlagen");
            Shutdown();
            return;
        }

        Log.Information("Update-Prüfung: {Count} Tweaks zurückgesetzt", ids.Count);
        if (!notice.ShouldNotify(ids))
        {
            if (ids.Count == 0)
            {
                notice.Remember(ids);
            }

            Shutdown();
            return;
        }

        notice.Remember(ids);
        var loc = _host.Services.GetRequiredService<LocalizationService>();
        var window = new Dialogs.ResetNoticeWindow(
            loc.Get("Reset.Notice.Heading"),
            string.Format(System.Globalization.CultureInfo.CurrentCulture, loc.Get("Reset.Notice.Body"), ids.Count),
            loc.Get("Reset.Notice.Apply"),
            loc.Get("RestartBar.Later"));
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Closed += (_, _) =>
        {
            if (window.ApplyChosen)
            {
                ShowMain(reapply: true);
            }
            else
            {
                Shutdown();
            }
        };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex gehörte dieser Instanz nicht (zweiter Start)
            }

            _singleInstance.Dispose();
        }

        Log.Information("Munz Quick Tweaks beendet");
        CrashLog.Close();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Fehler in der Oberfläche", e.Exception);
        var text = $"Es ist ein unerwarteter Fehler aufgetreten.\n\n{e.Exception.GetBaseException().Message}\n\nDetails stehen in:\n{CrashLog.FilePath}";

        // Meldung immer sichtbar: am sichtbaren Hauptfenster, sonst im Vordergrund des Desktops
        if (MainWindow is { IsVisible: true } owner)
        {
            MessageBox.Show(owner, text, "Munz Quick Tweaks", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(text, "Munz Quick Tweaks", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
        }
        e.Handled = true;
    }
}
