using Vortice.D3DCompiler;

namespace MunzQuickTweaks.Windows;

/// <summary>
/// Übersetzt einen HLSL-Pixel-Shader zur Laufzeit (d3dcompiler_47.dll ist Teil von Windows 10 und 11).
/// So braucht der Build kein Windows-SDK. Null, wenn der Compiler fehlt oder der Shader fehlerhaft ist.
/// </summary>
public static class ShaderCompiler
{
    public static byte[]? CompilePixelShader(string hlsl, string entryPoint = "main")
    {
        try
        {
            return Compiler.Compile(hlsl, entryPoint, "munz-glass", "ps_3_0").ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
