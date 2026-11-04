using System.Windows;
using System.Windows.Media.Animation;
using MunzQuickTweaks.App.Services;
using MunzQuickTweaks.App.ViewModels;

namespace MunzQuickTweaks.App;

public partial class MainWindow : Window
{
    // Logo-Animation auf 1,43 s beschleunigt, damit der Startbildschirm unter 1,5 s bleibt
    private const double SplashSpeed = 1.33;
    private static readonly TimeSpan SplashDuration = TimeSpan.FromSeconds(1.5);

    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settings;

    public MainWindow(MainViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizedMargin();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.NavigateCommand.ExecuteAsync(_viewModel.Items[0]);

        if (!_settings.Current.AnimationsEnabled)
        {
            Splash.Visibility = Visibility.Collapsed;
            return;
        }

        SplashLogo.Play(SplashSpeed);
        SideLogo.Play(SplashSpeed);
        await Task.Delay(SplashDuration);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25)) { EasingFunction = new CubicEase() };
        fade.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, fade);
    }

    // Maximiert ragt ein randloses Fenster über den Bildschirm hinaus: Rand ausgleichen
    private void UpdateMaximizedMargin()
    {
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
