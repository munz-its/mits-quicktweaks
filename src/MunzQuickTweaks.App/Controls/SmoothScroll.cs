using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MunzQuickTweaks.App.Controls;

/// <summary>
/// Weiches Scrollen mit dem Mausrad: Ziel wird gesammelt und pro Bild sanft angefahren.
/// Innere scrollbare Bereiche (Listen, Textfelder) behalten ihr eigenes Verhalten.
/// Ohne Animationen (Einstellung) bleibt das normale Windows-Scrollen.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(SmoothScroll));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    /// <summary>Laufende Bewegung abbrechen, z. B. beim Seitenwechsel.</summary>
    public static void Reset(ScrollViewer viewer)
    {
        if (viewer.GetValue(StateProperty) is State state)
        {
            state.Stop();
            state.Target = 0;
        }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            var state = new State(viewer);
            viewer.SetValue(StateProperty, state);
            viewer.PreviewMouseWheel += state.OnWheel;
            viewer.Unloaded += (_, _) => state.Stop();
        }
        else if (viewer.GetValue(StateProperty) is State state)
        {
            viewer.PreviewMouseWheel -= state.OnWheel;
            state.Stop();
            viewer.ClearValue(StateProperty);
        }
    }

    private sealed class State(ScrollViewer viewer)
    {
        private bool _running;
        private TimeSpan _last;

        public double Target { get; set; }

        public void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (!UiMotion.Enabled || e.Handled || InnerScrollerTakes(e))
            {
                return;
            }

            e.Handled = true;
            var start = _running ? Target : viewer.VerticalOffset;
            Target = Math.Clamp(start - (e.Delta * 0.9), 0, viewer.ScrollableHeight);
            if (!_running)
            {
                _running = true;
                _last = TimeSpan.Zero;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        public void Stop()
        {
            if (_running)
            {
                _running = false;
                CompositionTarget.Rendering -= OnFrame;
            }
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
            if (now == _last)
            {
                return; // gleiches Bild, nichts zu tun
            }

            // Bildratenunabhängig: pro 1/60 s etwa 22 % des Restwegs
            var dt = _last == TimeSpan.Zero ? 1.0 / 60 : Math.Min(0.1, (now - _last).TotalSeconds);
            _last = now;

            Target = Math.Clamp(Target, 0, viewer.ScrollableHeight);
            var current = viewer.VerticalOffset;
            var rest = Target - current;
            if (Math.Abs(rest) < 0.5)
            {
                viewer.ScrollToVerticalOffset(Target);
                Stop();
                return;
            }

            var factor = 1 - Math.Pow(1 - 0.22, dt * 60);
            viewer.ScrollToVerticalOffset(current + (rest * factor));
        }

        // Liegt unter der Maus ein eigener scrollbarer Bereich, der noch scrollen kann, darf der zuerst
        private bool InnerScrollerTakes(MouseWheelEventArgs e)
        {
            var node = e.OriginalSource as DependencyObject;
            while (node is not null && !ReferenceEquals(node, viewer))
            {
                if (node is ScrollViewer inner && inner.ScrollableHeight > 0)
                {
                    var canMove = e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;
                    if (canMove)
                    {
                        return true;
                    }
                }

                if (node is ComboBox { IsDropDownOpen: true })
                {
                    return true;
                }

                node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }

            return false;
        }
    }
}
