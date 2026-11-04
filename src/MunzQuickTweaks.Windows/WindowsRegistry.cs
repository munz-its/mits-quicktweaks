using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MunzQuickTweaks.Core.Tweaks;
using CoreHive = MunzQuickTweaks.Core.Tweaks.RegistryHive;
using WinHive = Microsoft.Win32.RegistryHive;

namespace MunzQuickTweaks.Windows;

/// <summary>Echter Registry-Zugriff, immer in der 64-Bit-Ansicht.</summary>
public sealed class WindowsRegistry : IRegistry
{
    public bool KeyExists(CoreHive hive, string key)
    {
        using var root = OpenRoot(hive);
        using var sub = root.OpenSubKey(key);
        return sub is not null;
    }

    public RegistryValue? Read(CoreHive hive, string key, string name)
    {
        using var root = OpenRoot(hive);
        using var sub = root.OpenSubKey(key);
        if (sub is null || !sub.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var raw = sub.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return sub.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => new RegistryValue(RegistryKind.DWord, unchecked((uint)(int)raw!).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new RegistryValue(RegistryKind.QWord, unchecked((ulong)(long)raw!).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.String => new RegistryValue(RegistryKind.String, (string)raw!),
            RegistryValueKind.ExpandString => new RegistryValue(RegistryKind.ExpandString, (string)raw!),
            // Andere Typen (Binary, MultiString) setzt die App nie: als "vorhanden, aber anders" behandeln
            var other => throw new NotSupportedException($"Registry-Typ {other} wird nicht unterstützt: {key}\\{name}"),
        };
    }

    public void Write(CoreHive hive, string key, string name, RegistryKind kind, string data)
    {
        using var root = OpenRoot(hive);
        using var sub = root.CreateSubKey(key, writable: true);
        switch (kind)
        {
            case RegistryKind.DWord:
                sub.SetValue(name, unchecked((int)uint.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                break;
            case RegistryKind.QWord:
                sub.SetValue(name, unchecked((long)ulong.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.QWord);
                break;
            case RegistryKind.String:
                sub.SetValue(name, data, RegistryValueKind.String);
                break;
            case RegistryKind.ExpandString:
                sub.SetValue(name, data, RegistryValueKind.ExpandString);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public void Delete(CoreHive hive, string key, string name)
    {
        using var root = OpenRoot(hive);
        using var sub = root.OpenSubKey(key, writable: true);
        sub?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKeyIfEmpty(CoreHive hive, string key)
    {
        using var root = OpenRoot(hive);
        using (var sub = root.OpenSubKey(key))
        {
            if (sub is null || sub.ValueCount > 0 || sub.SubKeyCount > 0)
            {
                return;
            }
        }

        root.DeleteSubKey(key, throwOnMissingSubKey: false);
    }

    private static RegistryKey OpenRoot(CoreHive hive) => RegistryKey.OpenBaseKey(
        hive == CoreHive.HKLM ? WinHive.LocalMachine : WinHive.CurrentUser,
        RegistryView.Registry64);
}

/// <summary>Sagt dem Explorer, dass sich Einstellungen geändert haben (z. B. Dateiendungen).</summary>
public sealed partial class WindowsNotifier : ISystemNotifier
{
    private const int ShcneAssocChanged = 0x08000000;
    private const int ShcnfIdList = 0x0000;
    private const int HwndBroadcast = 0xFFFF;
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;

    public void RefreshExplorer()
    {
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero, "ShellState", SmtoAbortIfHung, 1000, out _);
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);
}
