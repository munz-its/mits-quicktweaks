using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MunzQuickTweaks.App.Controls;

/// <summary>Globale Animations-Einstellung, wird von den Einstellungen gesetzt.</summary>
public static class UiMotion
{
    public static bool Enabled { get; set; } = true;
}

/// <summary>
/// Blauer Pixel-Wisch wie beim Cookie-Banner auf munz-its.de: sechs Pixel laufen von rechts nach links
/// und verblassen, der hellste vorne. Startet bei jeder Änderung von <see cref="Trigger"/>.
/// </summary>
public sealed class PixelWipe : StackPanel
{
    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger), typeof(int), typeof(PixelWipe), new PropertyMetadata(0, OnTriggerChanged));

    private static readonly double[] Opacities = { 0.9, 0.7, 0.5, 0.35, 0.2, 0.1 };

    public PixelWipe()
    {
        Orientation = Orientation.Horizontal;
        IsHitTestVisible = false;
        Opacity = 0;
        RenderTransform = new TranslateTransform();

        var blue = (Brush)Application.Current.FindResource("Blue");
        foreach (var opacity in Opacities)
        {
            Children.Add(new Rectangle { Width = 8, Height = 8, Fill = blue, Opacity = opacity, Margin = new Thickness(0, 0, 5, 0) });
        }
    }

    public int Trigger
    {
        get => (int)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    private static void OnTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PixelWipe wipe && (int)e.NewValue > 0 && UiMotion.Enabled)
        {
            wipe.Play();
        }
    }

    private void Play()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.1))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
        BeginAnimation(OpacityProperty, fade);

        var move = new DoubleAnimation(40, -120, TimeSpan.FromSeconds(0.9)) { EasingFunction = ease };
        ((TranslateTransform)RenderTransform).BeginAnimation(TranslateTransform.XProperty, move);
    }
}
