using System.Windows;
using System.Windows.Documents;
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

    // Erbt die Textfarbe der Umgebung: in der Navigation wechselt das Icon gleitend mit der Schrift
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(PixelIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

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

    /// <summary>Apple-Variante: bekannte Muster werden als Symbol der Windows-Symbolschrift gezeichnet statt als Pixel.</summary>
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["00100/01110/11111/01110/01010"] = "\uE734",
        ["01110/11111/10101/11111/01010"] = "\uE7FC",
        ["01110/10001/11111/11011/11111"] = "\uE72E",
        ["01110/10101/10111/10001/01110"] = "\uE777",
        ["00110/01100/11111/00110/01100"] = "\uE945",
        ["10001/01010/00100/01010/10001"] = "\uE711",
        ["01110/11111/10001/11111/11111"] = "\uE83F",
        ["11111/10001/11111/10001/11111"] = "\uE8A5",
        ["11111/10001/11111/00100/01110"] = "\uE7F4",
        ["11111/11011/10101/10001/11111"] = "\uE715",
    };

    private static readonly Typeface SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    protected override void OnRender(DrawingContext dc)
    {
        if (Pattern is { } key && Glyphs.TryGetValue(key, out var glyph))
        {
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                SymbolFont, Math.Min(ActualWidth, ActualHeight), Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
            return;
        }

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
