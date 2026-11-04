using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.App.ViewModels;
using MunzQuickTweaks.Core;
using MunzQuickTweaks.Core.Settings;
using MunzQuickTweaks.Windows;

namespace MunzQuickTweaks.App;

public partial class App : Application
{
    // Nur eine Instanz gleichzeitig: zwei Instanzen könnten sich bei Tweaks gegenseitig überschreiben
    private const string MutexName = @"Global\MunzITService.MunzQuickTweaks";

    private Mutex? _singleInstance;
    private IHost? _host;

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
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("Unbehandelter Fehler", (Exception)args.ExceptionObject);

        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(new SettingsStore(AppPaths.SettingsFile));
                services.AddSingleton<SettingsService>();
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<SystemInfoProvider>();

                services.AddSingleton<DashboardViewModel>();
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

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Fehler in der Oberfläche", e.Exception);
        MessageBox.Show(
            $"Es ist ein unerwarteter Fehler aufgetreten.\n\nDetails stehen in:\n{CrashLog.FilePath}",
            "Munz Quick Tweaks", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
