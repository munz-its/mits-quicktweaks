using System.ComponentModel;
using System.Runtime.InteropServices;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Windows;

/// <summary>Energiepläne über powrprof.dll (dieselbe API, die powercfg.exe nutzt).</summary>
public sealed partial class WindowsPowerPlans : IPowerPlans
{
    private const int AccessScheme = 16;
    private const int ErrorNoMoreItems = 259;
    private const int SystemPowerCapabilities = 4;
    private const int AoAcOffset = 20;
    private const int CapabilitiesSize = 76;

    private readonly Lazy<bool> _supported = new(() => !IsModernStandby());

    public bool IsSupported => _supported.Value;

    public Guid GetActive()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    public bool Exists(Guid scheme) => Enumerate().Contains(scheme);

    public Guid Duplicate(Guid template)
    {
        var target = IntPtr.Zero;
        Check(PowerDuplicateScheme(IntPtr.Zero, ref template, ref target));
        try
        {
            return Marshal.PtrToStructure<Guid>(target);
        }
        finally
        {
            LocalFree(target);
        }
    }

    public void SetActive(Guid scheme) => Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));

    public void Delete(Guid scheme) => Check(PowerDeleteScheme(IntPtr.Zero, ref scheme));

    private static List<Guid> Enumerate()
    {
        var result = new List<Guid>();
        var size = (uint)Marshal.SizeOf<Guid>();
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            for (uint index = 0; ; index++)
            {
                var length = size;
                var code = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref length);
                if (code == ErrorNoMoreItems)
                {
                    break;
                }

                Check(code);
                result.Add(Marshal.PtrToStructure<Guid>(buffer));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    /// <summary>Modern Standby (AoAc, "Always on, always connected"): eigene Energiepläne sind dort nicht vorgesehen.</summary>
    private static bool IsModernStandby()
    {
        var buffer = Marshal.AllocHGlobal(CapabilitiesSize);
        try
        {
            var status = CallNtPowerInformation(SystemPowerCapabilities, IntPtr.Zero, 0, buffer, CapabilitiesSize);
            return status == 0 && Marshal.ReadByte(buffer, AoAcOffset) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Check(uint code)
    {
        if (code != 0)
        {
            throw new Win32Exception((int)code);
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerDuplicateScheme(IntPtr rootPowerKey, ref Guid sourceSchemeGuid, ref IntPtr destinationSchemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerDeleteScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    private static partial uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, IntPtr outputBuffer, uint outputBufferLength);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
