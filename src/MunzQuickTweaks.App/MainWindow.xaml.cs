using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
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
        SourceInitialized += (_, _) => ApplyBackdrop();

        StartHitchWatch();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            {
                ShowPage(viewModel.CurrentPage);
                AnimatePageIn();
            }
        };
        viewModel.SelectionChanged += (_, item) => UpdateNavIndicator(item, animate: true);
        viewModel.BeforePageChange = FadePageOutAsync;
        viewModel.AfterPageShown = () => PageInCompleted;
        SizeChanged += (_, _) => UpdateNavIndicator(CurrentNavItem(), animate: false);
        StateChanged += (_, _) => UpdateMaximizedMargin();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.NavigateCommand.ExecuteAsync(_viewModel.Items[0]);

        if (!_settings.Current.AnimationsEnabled)
        {
            Splash.Visibility = Visibility.Collapsed;
            _ = PrebuildPagesAsync();
            return;
        }

        SplashLogo.Play(SplashSpeed);
        SideLogo.Play(SplashSpeed);
        await Task.Delay(SplashDuration);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25)) { EasingFunction = new CubicEase() };
        fade.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, fade);
        _ = PrebuildPagesAsync();
    }

    private static readonly TimeSpan GlideDuration = TimeSpan.FromSeconds(0.38);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(0.26);

    // Während des Übergangs wird die Seite als Bild zwischengespeichert. Sonst zeichnet WPF jede Karte in jedem Bild neu
    private readonly BitmapCache _transitionCache = new() { SnapsToDevicePixels = true };
    private TaskCompletionSource? _pageInDone;

    /// <summary>Fertig, sobald die neue Seite eingeblendet ist. Erst danach lädt die Seite ihre Daten nach.</summary>
    public Task PageInCompleted => _pageInDone?.Task ?? Task.CompletedTask;

    private void BeginTransition()
    {
        _transitionCache.RenderAtScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        PageHost.CacheMode = _transitionCache;
    }

    private void EndTransition()
    {
        // Danach wieder normal und gestochen scharf zeichnen
        PageHost.CacheMode = null;
    }

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
        BeginTransition();
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
            EndTransition();
            return;
        }

        // Neue Seite erst fertig anordnen, dann animieren: so fällt die Arbeit nicht mitten in die Bewegung
        PageHost.UpdateLayout();
        _pageInDone?.TrySetResult();
        var done = new TaskCompletionSource();
        _pageInDone = done;

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        BeginTransition();
        var fade = new DoubleAnimation(0, 1, FadeInDuration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(_pageInDone, done))
            {
                EndTransition();
            }

            done.TrySetResult();
        };
        PageHost.BeginAnimation(OpacityProperty, fade);
        Transform(PageHost).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, FadeInDuration) { EasingFunction = ease });
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

    /// <summary>
    /// Apple-Variante: unter Windows 11 (ab 22H2) Mica-Glas hinter der Seitenleiste, dunkle Fensterrahmen und runde Ecken.
    /// Windows 10 kennt das nicht, dort bleibt alles fest und dunkel.
    /// </summary>
    private void ApplyBackdrop()
    {
        if (!Controls.UiDesign.IsLiquidGlass || Environment.OSVersion.Version.Build < 22621)
        {
            return;
        }

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            var round = 2;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
            // Acrylic (3): zeichnet Desktop und Fenster dahinter live weich, mehr Glas als Mica
            var acrylic = 3;
            if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref acrylic, sizeof(int)) != 0)
            {
                return;
            }

            // Das Glas wird nur sichtbar, wo WPF nichts malt: Fenster durchsichtig, Inhalt fest, Seitenleiste leicht getönt
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(-1);
            Background = Brushes.Transparent;
            SidebarBg.Background = new SolidColorBrush(Color.FromArgb(0x38, 0x14, 0x15, 0x18));
            RootBorder.BorderThickness = new Thickness(0);
            if (Splash.Background is SolidColorBrush)
            {
                Splash.Background = (Brush)FindResource("Bg");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            // Ohne Glas weiter
        }
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Maximiert ragt ein randloses Fenster über den Bildschirm hinaus: Rand ausgleichen
    private void UpdateMaximizedMargin()
    {
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    /// <summary>Großer Titel scrollt weg, der kleine Titel in der Glasleiste blendet passend ein.</summary>
    // Stockt die Oberfläche spürbar, landet das mit Seite und Dauer im Log. So lässt sich gezielt nachbessern
    private void StartHitchWatch()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var last = 0L;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var now = watch.ElapsedMilliseconds;
            var gap = now - last - 50;
            last = now;
            if (gap > 150 && IsVisible)
            {
                Serilog.Log.Warning("Oberfläche stockte {Ms} ms, Seite {Page}, Design {Design}",
                    gap, _viewModel.CurrentPage?.GetType().Name ?? "-", Controls.UiDesign.IsLiquidGlass ? "Liquid Glass" : "Munz");
            }
        };
        timer.Start();
    }

    // Fertig gebaute Seiten: ein Seitenwechsel zeigt nur noch die vorhandene Seite, statt sie jedes Mal neu aufzubauen
    private readonly Dictionary<object, FrameworkElement> _pages = new();

    private void ShowPage(object? page)
    {
        if (page is null)
        {
            PageHost.Content = null;
            return;
        }

        var view = BuildPage(page);
        Controls.SmoothScroll.Reset(ContentScroller);
        ContentScroller.ScrollToTop();
        PageHost.Content = view;
    }

    /// <summary>Baut die übrigen Seiten nach dem Start im Leerlauf vor, eine nach der anderen. Der erste Wechsel ist dann sofort da.</summary>
    private async Task PrebuildPagesAsync()
    {
        foreach (var page in _viewModel.Items.Select(i => i.Page).Append(_viewModel.SettingsItem.Page).ToList())
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_pages.ContainsKey(page))
                {
                    return;
                }

                var view = BuildPage(page);
                view.DataContext = page;
                view.Measure(new Size(Math.Max(300, PageHost.ActualWidth), double.PositiveInfinity));
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    // Wie die normale Vorlagensuche: auch Basistypen prüfen (z. B. Datenschutz nutzt die Vorlage der Tweaks-Seite)
    private DataTemplate? FindPageTemplate(Type? type)
    {
        for (; type is not null && type != typeof(object); type = type.BaseType)
        {
            if (TryFindResource(new DataTemplateKey(type)) is DataTemplate template)
            {
                return template;
            }
        }

        return null;
    }

    private FrameworkElement BuildPage(object page)
    {
        if (!_pages.TryGetValue(page, out var view))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (FindPageTemplate(page.GetType()) is { } template && template.LoadContent() is FrameworkElement built)
            {
                built.DataContext = page;
                view = built;
            }
            else
            {
                view = new ContentPresenter { Content = page };
            }

            _pages[page] = view;
            Serilog.Log.Information("Seite {Page} gebaut in {Ms} ms", page.GetType().Name, watch.ElapsedMilliseconds);
        }

        return view;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
