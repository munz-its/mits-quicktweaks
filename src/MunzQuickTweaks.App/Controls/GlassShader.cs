using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MunzQuickTweaks.App.Controls;

/// <summary>
/// Liquid-Glass-Shader: bricht den weichgezeichneten Hintergrund an den Kanten wie eine Linse,
/// legt eine leichte Tönung darüber und zeichnet Lichtkanten oben links und einen Glanz nach unten.
/// Schneidet die Fläche auf ein abgerundetes Rechteck zu.
/// </summary>
public sealed class GlassShader : ShaderEffect
{
    private const string Hlsl = """
        sampler2D input : register(s0);
        float2 size : register(c0);
        float radius : register(c1);
        float strength : register(c2);
        float4 tint : register(c3);
        float rimLight : register(c4);

        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 p = uv * size;
            float2 halfSize = size * 0.5;
            float2 q = abs(p - halfSize) - (halfSize - radius);
            float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

            // Linse: je näher am Rand, desto stärker zieht das Glas den Hintergrund nach innen
            float band = 22.0;
            float edge = saturate(1.0 + dist / band);
            float2 dir = (p - halfSize) / max(length(p - halfSize), 0.001);
            float2 offset = -dir * edge * edge * strength;
            float2 sampleUv = clamp((p + offset) / size, 0.001, 0.999);
            float4 color = tex2D(input, sampleUv);

            // Chromatische Aberration ganz am Rand, wie bei echtem Glas
            float4 red = tex2D(input, clamp((p + offset * 1.12) / size, 0.001, 0.999));
            color.r = lerp(color.r, red.r, edge * 0.6);

            // Tönung (Glas hat Eigenfarbe)
            color.rgb = lerp(color.rgb, tint.rgb * color.a, tint.a);

            // Lichtkante: hell oben links, dunkler unten rechts
            float rim = saturate(1.0 - abs(dist + 1.2) / 1.4);
            float light = saturate(dot(-dir, normalize(float2(-0.45, -1.0))) * 0.5 + 0.5);
            color.rgb += rim * rimLight * (0.15 + 0.85 * light * light) * color.a;

            // Innerer Glanz knapp unter der Oberkante
            float sheen = saturate(1.0 - (p.y / (size.y * 0.55)));
            color.rgb += sheen * sheen * 0.05 * color.a;

            // Zuschnitt auf das abgerundete Rechteck, weich auf 1 px
            float mask = saturate(0.5 - dist);
            return color * mask;
        }
        """;

    private static readonly Lazy<PixelShader?> Shared = new(Create);

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(GlassShader), 0);
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(Point), typeof(GlassShader),
        new UIPropertyMetadata(new Point(100, 100), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(GlassShader),
        new UIPropertyMetadata(16.0, PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(nameof(Strength), typeof(double), typeof(GlassShader),
        new UIPropertyMetadata(14.0, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(GlassShader),
        new UIPropertyMetadata(Color.FromArgb(0x38, 0x1E, 0x1F, 0x23), PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty RimLightProperty = DependencyProperty.Register(nameof(RimLight), typeof(double), typeof(GlassShader),
        new UIPropertyMetadata(0.45, PixelShaderConstantCallback(4)));

    private GlassShader(PixelShader shader)
    {
        PixelShader = shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(SizeProperty);
        UpdateShaderValue(RadiusProperty);
        UpdateShaderValue(StrengthProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(RimLightProperty);
    }

    /// <summary>Null, wenn der Shader auf diesem PC nicht übersetzt werden kann. Dann bleibt das Glas ohne Brechung.</summary>
    public static GlassShader? TryCreate() =>
        RenderCapability.IsPixelShaderVersionSupported(3, 0) && Shared.Value is { } shader ? new GlassShader(shader) : null;

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public Point Size
    {
        get => (Point)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public Color Tint
    {
        get => (Color)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public double RimLight
    {
        get => (double)GetValue(RimLightProperty);
        set => SetValue(RimLightProperty, value);
    }

    private static PixelShader? Create()
    {
        // Glas darf nie den Start der App verhindern: jeder Fehler heißt einfach "ohne Brechung"
        try
        {
            var bytes = MunzQuickTweaks.Windows.ShaderCompiler.CompilePixelShader(Hlsl);
            if (bytes is null)
            {
                return null;
            }

            var shader = new PixelShader();
            shader.SetStreamSource(new MemoryStream(bytes));
            return shader;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
