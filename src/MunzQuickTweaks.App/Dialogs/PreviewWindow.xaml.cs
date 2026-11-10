using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MunzQuickTweaks.App.Dialogs;

/// <summary>
/// Zusammenfassung vor einem Profil oder vor "Alles zurücksetzen": ein Blatt gleitet über die abgedunkelte App.
/// Jeder Punkt und jeder Bereich ist abwählbar. Esc oder Klick daneben bricht ab.
/// </summary>
public partial class PreviewWindow : Window
{
    private static readonly Duration InTime = new(TimeSpan.FromMilliseconds(260));
    private static readonly Duration OutTime = new(TimeSpan.FromMilliseconds(160));
    private bool _closing;

    public PreviewWindow(PreviewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Loaded += (_, _) => AnimateIn();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(false);
            }
        };
    }

    /// <returns>True, wenn bestätigt und mindestens ein Punkt gewählt ist.</returns>
    public static bool Ask(PreviewModel model)
    {
        var owner = Application.Current?.MainWindow;
        var window = new PreviewWindow(model) { Owner = owner };
        window.CoverOwner(owner);
        window.UseSnapshotOf(owner);
        return window.ShowDialog() == true && model.Sections.Any(s => s.Items.Any(i => i.IsChecked) && s.IsEnabled);
    }

    /// <summary>Legt sich genau über das App-Fenster, auch wenn es maximiert ist.</summary>
    private void CoverOwner(Window? owner)
    {
        Rect area;
        if (owner is null)
        {
            area = SystemParameters.WorkArea;
        }
        else if (owner.WindowState == WindowState.Maximized)
        {
            area = SystemParameters.WorkArea;
        }
        else
        {
            area = new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
        }

        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
        Sheet.MaxHeight = Math.Max(320, area.Height - 72);
        Sheet.Width = Math.Min(620, area.Width - 48);
    }

    /// <summary>Schnappschuss der App für das Glas im Blatt. Die App ändert sich nicht, solange das Blatt offen ist.</summary>
    private void UseSnapshotOf(Window? owner)
    {
        if (owner is null || !Controls.GlassPanel.Enabled || owner.ActualWidth < 1)
        {
            return;
        }

        try
        {
            var dpi = VisualTreeHelper.GetDpi(owner);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(owner.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(owner.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(owner);
            bitmap.Freeze();
            SheetGlass.Backdrop = bitmap;
            Sheet.LayoutUpdated += PlaceGlass;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or InvalidOperationException)
        {
            // Ohne Schnappschuss bleibt das Blatt eine getönte Fläche
        }
    }

    private void PlaceGlass(object? sender, EventArgs e)
    {
        if (Sheet.ActualWidth < 1)
        {
            return;
        }

        Sheet.LayoutUpdated -= PlaceGlass;

        // Position ohne die Einblend-Verschiebung: so liegt das Glas genau über der Stelle, an der das Blatt landet
        var left = (ActualWidth - Sheet.ActualWidth) / 2;
        var top = Sheet.Margin.Top;
        SheetGlass.SetBackdropArea(new Rect(left, top, Sheet.ActualWidth, Sheet.ActualHeight));
    }

    private void AnimateIn()
    {
        if (!Controls.UiMotion.Enabled)
        {
            Scrim.Opacity = 1;
            Sheet.Opacity = 1;
            SheetMove.Y = 0;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, InTime));
        Sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(1, InTime) { EasingFunction = ease });
        SheetMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(0, InTime) { EasingFunction = ease });
    }

    /// <summary>Zurück auf demselben Weg, auf dem das Blatt gekommen ist.</summary>
    private void Finish(bool result)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (!Controls.UiMotion.Enabled)
        {
            DialogResult = result;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, OutTime) { EasingFunction = ease };
        fade.Completed += (_, _) => DialogResult = result;
        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, OutTime));
        SheetMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(-20, OutTime) { EasingFunction = ease });
        Sheet.BeginAnimation(OpacityProperty, fade);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => Finish(true);

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(false);

    private void OnScrimClick(object sender, MouseButtonEventArgs e) => Finish(false);
}
