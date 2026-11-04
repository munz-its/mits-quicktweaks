using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MunzQuickTweaks.App.Controls;

/// <summary>
/// Das 3D-Pixel-M von Munz IT Service (dunkle Version), übernommen aus mits-landingpage/components/Logo.tsx.
/// Play() startet die freigegebene Animation: Blöcke bauen sich auf, das blaue Pixel fällt rein und blinkt.
/// </summary>
public sealed class PixelLogo : Viewbox
{
    // [x, y, Verzögerung in s] der weißen Blöcke, identisch zur Webseite
    private static readonly (double X, double Y, double Delay)[] Blocks =
    {
        (5, 77, 0), (77, 77, 0.7), (5, 59, 0.05), (77, 59, 0.65), (5, 41, 0.1), (77, 41, 0.6),
        (5, 23, 0.15), (23, 23, 0.3), (59, 23, 0.4), (77, 23, 0.55), (5, 5, 0.2), (77, 5, 0.5),
    };

    private readonly List<(Canvas Group, double Delay)> _blocks = new();
    private readonly Canvas _pixel;

    public PixelLogo()
    {
        // viewBox="0 -8 104 104": Canvas 104 x 104, Inhalt um 8 nach unten verschoben
        var canvas = new Canvas { Width = 104, Height = 104 };

        foreach (var (x, y, delay) in Blocks)
        {
            var group = CreateCube(x, y + 8, "#FFFFFF", "#6F7889", "#D9DEE6");
            canvas.Children.Add(group);
            _blocks.Add((group, delay));
        }

        _pixel = CreateCube(41, 49, "#84A6FF", "#143DA6", "#1F5EFF");
        canvas.Children.Add(_pixel);

        Child = canvas;
        Stretch = Stretch.Uniform;
        SnapsToDevicePixels = true;
    }

    /// <summary>Spielt die Aufbau-Animation einmal ab.</summary>
    /// <param name="speed">1 = Originaltempo der Webseite (1,9 s), 1,3 = schneller.</param>
    public void Play(double speed = 1.0)
    {
        foreach (var (group, delay) in _blocks)
        {
            AnimateBlock(group, delay / speed, 0.55 / speed);
        }

        AnimatePixel(_pixel, 1.9 / speed);
    }

    private static Canvas CreateCube(double x, double y, string top, string side, string front)
    {
        var group = new Canvas
        {
            RenderTransformOrigin = new Point(0.5, 1),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
        };
        group.Children.Add(Polygon(top, (x, y), (x + 5, y - 5), (x + 19, y - 5), (x + 14, y)));
        group.Children.Add(Polygon(side, (x + 14, y), (x + 19, y - 5), (x + 19, y + 9), (x + 14, y + 14)));
        var rect = new Rectangle { Width = 14, Height = 14, Fill = BrushFrom(front) };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        group.Children.Add(rect);
        return group;
    }

    private static Polygon Polygon(string color, params (double X, double Y)[] points)
    {
        var polygon = new Polygon { Fill = BrushFrom(color) };
        foreach (var (px, py) in points)
        {
            polygon.Points.Add(new Point(px, py));
        }

        return polygon;
    }

    private static SolidColorBrush BrushFrom(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static void AnimateBlock(Canvas group, double delay, double duration)
    {
        var begin = TimeSpan.FromSeconds(delay);
        var length = new Duration(TimeSpan.FromSeconds(duration));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        var transforms = (TransformGroup)group.RenderTransform;
        var scale = (ScaleTransform)transforms.Children[0];
        var move = (TranslateTransform)transforms.Children[1];

        group.Opacity = 0;
        group.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, length) { BeginTime = begin });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, length) { BeginTime = begin, EasingFunction = ease });
        var grow = new DoubleAnimation(0.3, 1, length) { BeginTime = begin, EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private static void AnimatePixel(Canvas pixel, double total)
    {
        // Keyframes wie @keyframes lpin der Webseite (Prozent der Gesamtdauer)
        KeyTime At(double percent) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(total * percent / 100));

        var move = (TranslateTransform)((TransformGroup)pixel.RenderTransform).Children[1];
        var fall = new DoubleAnimationUsingKeyFrames();
        fall.KeyFrames.Add(new DiscreteDoubleKeyFrame(-80, At(0)));
        fall.KeyFrames.Add(new DiscreteDoubleKeyFrame(-80, At(42)));
        fall.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(58), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        fall.KeyFrames.Add(new EasingDoubleKeyFrame(-9, At(64)));
        fall.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(70)));
        fall.KeyFrames.Add(new EasingDoubleKeyFrame(-3, At(74)));
        fall.KeyFrames.Add(new EasingDoubleKeyFrame(0, At(78)));
        move.BeginAnimation(TranslateTransform.YProperty, fall);

        var blink = new DoubleAnimationUsingKeyFrames();
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, At(0)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, At(42)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(58)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(78)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.15, At(84)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(89)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.15, At(94)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, At(100)));
        pixel.BeginAnimation(OpacityProperty, blink);
    }
}
