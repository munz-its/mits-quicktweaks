using System.Diagnostics;
using MunzQuickTweaks.Core.Tools;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace MunzQuickTweaks.Windows;

/// <summary>
/// Grafik-Test über DirectX 11: ein Vollbild-Dreieck mit rechenintensivem Pixel-Shader in Full HD,
/// ohne Fenster. Gezählt werden fertig gerenderte Bilder pro Sekunde auf der leistungsstärksten Grafikkarte.
/// </summary>
public sealed class WindowsGpuBenchmark : IGpuBenchmark
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int DrawsPerFrame = 4;

    private const string Shader = """
        struct VsOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VsOut VS(uint id : SV_VertexID)
        {
            VsOut o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        float4 PS(VsOut i) : SV_TARGET
        {
            float2 c = i.uv * float2(3.0, 2.0) - float2(2.0, 1.0);
            float2 z = 0;
            float n = 0;
            [loop] for (int k = 0; k < 96; k++)
            {
                z = float2(z.x * z.x - z.y * z.y, 2 * z.x * z.y) + c;
                n += dot(z, z) < 4 ? 1 : 0;
            }
            float v = n / 96.0;
            return float4(v, sin(v * 6.28), cos(v * 3.14), 1);
        }
        """;

    public (string Name, double Fps) Run(TimeSpan duration, CancellationToken cancel)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
        factory.EnumAdapterByGpuPreference(0, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).CheckError();
        using (adapter)
        {
            var name = adapter!.Description1.Description.Trim();

            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None,
                new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
            using (device)
            using (context)
            {
                var vsCode = Compiler.Compile(Shader, "VS", "munz-gpu", "vs_5_0");
                var psCode = Compiler.Compile(Shader, "PS", "munz-gpu", "ps_5_0");
                using var vs = device!.CreateVertexShader(vsCode.Span);
                using var ps = device.CreatePixelShader(psCode.Span);

                using var texture = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget));
                using var target = device.CreateRenderTargetView(texture);
                using var done = device.CreateQuery(new QueryDescription(QueryType.Event));

                context!.OMSetRenderTargets(target);
                context.RSSetViewport(new Viewport(Width, Height));
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                context.VSSetShader(vs);
                context.PSSetShader(ps);

                // Kurz aufwärmen, damit Takt und Treiber-Caches stehen
                RenderFrames(context, target, done, TimeSpan.FromSeconds(1), cancel);
                var (frames, seconds) = RenderFrames(context, target, done, duration, cancel);
                return (name, Math.Round(frames / seconds, 1));
            }
        }
    }

    private static (long Frames, double Seconds) RenderFrames(ID3D11DeviceContext context, ID3D11RenderTargetView target, ID3D11Query done, TimeSpan duration, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        long frames = 0;
        while (watch.Elapsed < duration)
        {
            cancel.ThrowIfCancellationRequested();
            context.ClearRenderTargetView(target, new Color4(0, 0, 0, 1));
            for (var i = 0; i < DrawsPerFrame; i++)
            {
                context.Draw(3, 0);
            }

            // Auf das fertige Bild warten, sonst misst der Test nur, wie schnell Befehle in die Warteschlange passen
            context.End(done);
            context.Flush();
            while (!context.IsDataAvailable(done))
            {
                cancel.ThrowIfCancellationRequested();
                Thread.SpinWait(50);
            }

            frames++;
        }

        return (frames, watch.Elapsed.TotalSeconds);
    }
}
