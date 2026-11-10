using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MunzQuickTweaks.App.Controls;

/// <summary>
/// Glasfläche: zeigt den Inhalt hinter sich weichgezeichnet, gebrochen und getönt.
/// Live-Modus: Source ist ein Element darunter (z. B. der scrollende Inhalt), das Glas folgt ihm Bild für Bild.
/// Schnappschuss-Modus: Backdrop ist ein Bild (z. B. die App hinter einem Dialog), Ausschnitt über SetBackdropArea.
/// Eigene Kinder (Text, Buttons) liegen über dem Glas.
/// </summary>
public sealed class GlassPanel : Grid
{
    private const double Pad = 40;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(Visual), typeof(GlassPanel),
        new PropertyMetadata(null, (d, _) => ((GlassPanel)d).Rebuild()));

    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(nameof(Backdrop), typeof(ImageSource), typeof(GlassPanel),
        new PropertyMetadata(null, (d, _) => ((GlassPanel)d).Rebuild()));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(GlassPanel),
        new PropertyMetadata(18.0, (d, _) => ((GlassPanel)d).UpdateShape()));

    public static readonly DependencyProperty BlurProperty = DependencyProperty.Register(nameof(Blur), typeof(double), typeof(GlassPanel),
        new PropertyMetadata(28.0, (d, e) => ((GlassPanel)d)._blur.Radius = (double)e.NewValue));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(GlassPanel),
        new PropertyMetadata(Color.FromArgb(0x40, 0x1E, 0x1F, 0x23), (d, _) => ((GlassPanel)d).UpdateShape()));

    public static readonly DependencyProperty RefractionProperty = DependencyProperty.Register(nameof(Refraction), typeof(double), typeof(GlassPanel),
        new PropertyMetadata(14.0, (d, _) => ((GlassPanel)d).UpdateShape()));

    /// <summary>Global aus, z. B. über die Einstellung "Glaseffekte". Dann bleibt eine getönte, feste Fläche.</summary>
    public static bool Enabled { get; set; } = true;

    private readonly Grid _host = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Rectangle _backdrop = new() { Margin = new Thickness(-Pad), IsHitTestVisible = false };
    private readonly Border _fallback = new() { IsHitTestVisible = false };
    private readonly Border _rim = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly BlurEffect _blur = new() { Radius = 28, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private readonly GlassShader? _shader = GlassShader.TryCreate();
    private Rect _lastViewbox;
    private bool _rendering;

    public GlassPanel()
    {
        _backdrop.Effect = _blur;
        _host.Children.Add(_backdrop);
        Children.Insert(0, _fallback);
        Children.Insert(1, _host);
        Children.Insert(2, _rim);

        _rim.BorderBrush = new LinearGradientBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 70);
        SizeChanged += (_, _) => UpdateShape();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
        UpdateShape();
    }

    public Visual? Source
    {
        get => (Visual?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ImageSource? Backdrop
    {
        get => (ImageSource?)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double Blur
    {
        get => (double)GetValue(BlurProperty);
        set => SetValue(BlurProperty, value);
    }

    public Color Tint
    {
        get => (Color)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public double Refraction
    {
        get => (double)GetValue(RefractionProperty);
        set => SetValue(RefractionProperty, value);
    }

    /// <summary>Schnappschuss-Modus: welcher Teil des Bildes hinter dem Glas liegt, in Bild-Koordinaten.</summary>
    public void SetBackdropArea(Rect area)
    {
        if (_backdrop.Fill is TileBrush brush)
        {
            brush.Viewbox = new Rect(area.X - Pad, area.Y - Pad, area.Width + 2 * Pad, area.Height + 2 * Pad);
        }
    }

    private void Rebuild()
    {
        var useGlass = Enabled && (Source is not null || Backdrop is not null);
        _host.Visibility = useGlass ? Visibility.Visible : Visibility.Collapsed;
        if (!useGlass)
        {
            _backdrop.Fill = null;
            UpdateShape();
            return;
        }

        TileBrush brush = Source is { } source
            ? new VisualBrush(source)
            : new ImageBrush(Backdrop);
        brush.ViewboxUnits = BrushMappingMode.Absolute;
        brush.Stretch = Stretch.Fill;
        brush.AlignmentX = AlignmentX.Left;
        brush.AlignmentY = AlignmentY.Top;
        _backdrop.Fill = brush;
        _lastViewbox = Rect.Empty;
        UpdateShape();
        Start();
    }

    private void UpdateShape()
    {
        var radius = CornerRadius;
        _rim.CornerRadius = new System.Windows.CornerRadius(radius);
        _fallback.CornerRadius = new System.Windows.CornerRadius(radius);

        var glassOn = _host.Visibility == Visibility.Visible;

        // Ohne Shader oder ohne Glas: feste, getönte Fläche, damit der Text lesbar bleibt
        _fallback.Background = new SolidColorBrush(glassOn && _shader is not null
            ? Colors.Transparent
            : Color.FromArgb(glassOn ? (byte)0xB8 : (byte)0xF2, Tint.R, Tint.G, Tint.B));

        if (_shader is not null && glassOn)
        {
            _shader.Size = new Point(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
            _shader.Radius = radius;
            _shader.Strength = Refraction;
            _shader.Tint = Tint;
            _host.Effect = _shader;
        }
        else
        {
            _host.Effect = null;
        }

        // Zuschnitt auch mit Shader: falls die Grafikkarte den Shader still ignoriert, bleibt die Form trotzdem rund
        _host.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)), radius, radius);
    }

    private void Start()
    {
        if (_rendering || Source is null || !IsLoaded || !IsVisible)
        {
            return;
        }

        _rendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (!_rendering)
        {
            return;
        }

        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>Live-Modus: Ausschnitt jedes Bild neu berechnen, damit das Glas beim Scrollen mitläuft.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (Source is not UIElement source || _backdrop.Fill is not VisualBrush brush || !source.IsVisible)
        {
            return;
        }

        try
        {
            var origin = TranslatePoint(new Point(-Pad, -Pad), source);
            var box = new Rect(origin, new Size(ActualWidth + 2 * Pad, ActualHeight + 2 * Pad));
            if (box != _lastViewbox)
            {
                brush.Viewbox = box;
                _lastViewbox = box;
            }
        }
        catch (InvalidOperationException)
        {
            // Nicht im selben Fenster: nichts zu tun
        }
    }
}
