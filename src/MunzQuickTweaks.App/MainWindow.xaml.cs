using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            {
                AnimatePageIn();
            }
        };
        viewModel.SelectionChanged += (_, item) => UpdateNavIndicator(item, animate: true);
        viewModel.BeforePageChange = FadePageOutAsync;
        SizeChanged += (_, _) => UpdateNavIndicator(CurrentNavItem(), animate: false);
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

    private static readonly TimeSpan GlideDuration = TimeSpan.FromSeconds(0.38);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.14);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(0.38);

    private bool _indicatorPlaced;

    // Seitenwechsel, Teil 1: alte Seite blendet kurz aus und hebt sich leicht an
    private Task FadePageOutAsync()
    {
        if (!Controls.UiMotion.Enabled)
        {
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        var ease = new SineEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(PageHost.Opacity, 0, FadeOutDuration) { EasingFunction = ease };
        fade.Completed += (_, _) => done.TrySetResult();
        PageHost.BeginAnimation(OpacityProperty, fade);
        Transform(PageHost).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -6, FadeOutDuration) { EasingFunction = ease });
        return done.Task;
    }

    // Seitenwechsel, Teil 2: neue Seite blendet weich ein und gleitet 12 Pixel nach oben
    private void AnimatePageIn()
    {
        if (!Controls.UiMotion.Enabled)
        {
            PageHost.BeginAnimation(OpacityProperty, null);
            PageHost.Opacity = 1;
            return;
        }

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeInDuration) { EasingFunction = ease });
        Transform(PageHost).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, FadeInDuration) { EasingFunction = ease });
    }

    private static TranslateTransform Transform(UIElement element) => (TranslateTransform)element.RenderTransform;

    private NavItem? CurrentNavItem() => _viewModel.Items.FirstOrDefault(i => i.IsSelected);

    /// <summary>Schiebt Auswahl-Hintergrund und blaues Pixel zum gewählten Eintrag.</summary>
    private void UpdateNavIndicator(NavItem? item, bool animate)
    {
        if (item is null)
        {
            return;
        }

        // Erst nach dem Layout messen, sonst stimmen die Positionen noch nicht
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var button = FindNavButton(item);
            if (button is null || button.ActualHeight <= 0)
            {
                return;
            }

            var top = button.TranslatePoint(new Point(0, 0), NavOverlay).Y;
            var height = button.ActualHeight;
            var markerTop = top + (height / 2) - 2;
            var smooth = animate && _indicatorPlaced && Controls.UiMotion.Enabled;

            if (smooth)
            {
                var ease = new QuarticEase { EasingMode = EasingMode.EaseInOut };
                NavPill.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, GlideDuration) { EasingFunction = ease });
                NavPill.BeginAnimation(HeightProperty, new DoubleAnimation(height, GlideDuration) { EasingFunction = ease });
                NavMarker.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(markerTop, GlideDuration) { EasingFunction = ease });
            }
            else
            {
                NavPill.BeginAnimation(Canvas.TopProperty, null);
                NavPill.BeginAnimation(HeightProperty, null);
                NavMarker.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetTop(NavPill, top);
                NavPill.Height = height;
                Canvas.SetTop(NavMarker, markerTop);
            }

            if (!_indicatorPlaced)
            {
                _indicatorPlaced = true;
                var appear = Controls.UiMotion.Enabled ? TimeSpan.FromSeconds(0.3) : TimeSpan.Zero;
                NavPill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, appear));
                NavMarker.BeginAnimation(OpacityProperty, new DoubleAnimation(1, appear));
            }
        });
    }

    private RadioButton? FindNavButton(NavItem item)
    {
        // Mit Gruppen liefert der Container-Generator nichts: Schalter über den Visual Tree suchen
        return FindChildren<RadioButton>(NavList).FirstOrDefault(b => ReferenceEquals(b.DataContext, item));
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
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
