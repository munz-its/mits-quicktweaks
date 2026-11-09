using System.Runtime.InteropServices;
using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Tweaks;

namespace MunzQuickTweaks.Windows;

/// <summary>
/// Energiemodus über die Overlay-Funktionen von powrprof.dll. Das ist derselbe Weg, den die
/// Windows-Einstellungen und der Akku-Regler in der Taskleiste von Windows 10 nutzen.
/// </summary>
public sealed partial class WindowsPowerModes : IPowerModes
{
    private readonly WindowsPowerPlans _plans = new();

    public bool ModeAvailable
    {
        get
        {
            try
            {
                return _plans.GetActive() == TweakEngine.BalancedScheme;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    public PowerMode? GetMode()
    {
        try
        {
            return PowerGetEffectiveOverlayScheme(out var overlay) == 0 ? PowerModeIds.FromGuid(overlay) : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void SetMode(PowerMode mode)
    {
        try
        {
            WindowsPowerPlans.Check(PowerSetActiveOverlayScheme(PowerModeIds.ToGuid(mode)));
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new NotSupportedException("Diese Windows-Version kennt keinen Energiemodus.", ex);
        }
    }

    /// <summary>Roher Overlay-Wert, auch für Modi, die die App nicht kennt.</summary>
    public static Guid? GetOverlayId()
    {
        try
        {
            return PowerGetEffectiveOverlayScheme(out var overlay) == 0 ? overlay : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public static void SetOverlayId(Guid overlay)
    {
        try
        {
            WindowsPowerPlans.Check(PowerSetActiveOverlayScheme(overlay));
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new NotSupportedException("Diese Windows-Version kennt keinen Energiemodus.", ex);
        }
    }

    public IReadOnlyList<PowerPlanInfo> ListPlans()
    {
        var active = _plans.GetActive();
        return WindowsPowerPlans.Enumerate()
            .Select(id => new PowerPlanInfo(id, FriendlyName(id), id == active))
            .ToList();
    }

    public void ActivatePlan(Guid id) => _plans.SetActive(id);

    private static string FriendlyName(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
        {
            return scheme.ToString();
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer) ?? scheme.ToString()
                : scheme.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);
}
