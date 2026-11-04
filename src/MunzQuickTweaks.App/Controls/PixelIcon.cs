using System.Windows;
using System.Windows.Media;

namespace MunzQuickTweaks.App.Controls;

/// <summary>
/// Zeichnet ein Pixel-Icon aus einem Muster wie "11011/11011/00000/11011/11011".
/// Passt zum Pixel-M-Logo und bleibt bei jeder Skalierung scharf.
/// </summary>
public sealed class PixelIcon : FrameworkElement
{
    public static readonly DependencyProperty PatternProperty = DependencyProperty.Register(
        nameof(Pattern), typeof(string), typeof(PixelIcon),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(PixelIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public PixelIcon()
    {
        SnapsToDevicePixels = true;
        Width = 15;
        Height = 15;
    }

    public string Pattern
    {
        get => (string)GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rows = (Pattern ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0)
        {
            return;
        }

        var columns = rows.Max(r => r.Length);
        var cell = Math.Min(ActualWidth / columns, ActualHeight / rows.Length);
        var gap = cell * 0.18;

        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] == '1')
                {
                    dc.DrawRectangle(Foreground, null, new Rect(x * cell, y * cell, cell - gap, cell - gap));
                }
            }
        }
    }
}
