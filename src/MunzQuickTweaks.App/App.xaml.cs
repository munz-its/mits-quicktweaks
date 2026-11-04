using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.App.ViewModels;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Settings;
using MunzQuickTweaks.Core.Tweaks;
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
                services.AddSingleton(new JournalStore(Path.Combine(AppPaths.MachineData, "state", "journal.json")));
                services.AddSingleton(new HistoryStore(Path.Combine(AppPaths.MachineData, "state", "history.jsonl")));
                services.AddSingleton<IRegistryBackup>(sp => new RegExeBackup(AppPaths.BackupDirectory, sp.GetRequiredService<IRegistry>()));
                services.AddSingleton<RestorePointGate>();
                services.AddSingleton(sp => new TweakEngine(
                    sp.GetRequiredService<IRegistry>(),
                    sp.GetRequiredService<JournalStore>(),
                    sp.GetRequiredService<ISystemNotifier>(),
                    sp.GetRequiredService<SystemInfoProvider>().ReadBuild(),
                    sp.GetRequiredService<IPowerPlans>())
                {
                    // Fremdes Admin-Konto: Benutzer-Tweaks würden im falschen Konto landen
                    UserHiveAllowed = !AccountInfo.IsForeignElevation(),
                });
                services.AddSingleton<TweakService>();

                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<TweaksViewModel>();
                services.AddSingleton<RestoreViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        var settings = _host.Services.GetRequiredService<SettingsService>();
        _host.Services.GetRequiredService<LocalizationService>().Apply(settings.Current.Language);

        var window = _host.Services.GetRequiredService<MainWindow>();
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
        MessageBox.Show(
            $"Es ist ein unerwarteter Fehler aufgetreten.\n\n{e.Exception.GetBaseException().Message}\n\nDetails stehen in:\n{CrashLog.FilePath}",
            "Munz Quick Tweaks", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
