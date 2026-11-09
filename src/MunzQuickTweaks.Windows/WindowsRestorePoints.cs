using System.Globalization;
using System.Management;
using Microsoft.Win32;
using MunzQuickTweaks.Core.Safety;

namespace MunzQuickTweaks.Windows;

/// <summary>Wiederherstellungspunkte über WMI (root\default, Klasse SystemRestore).</summary>
public sealed class WindowsRestorePoints : IRestorePointService
{
    private const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string SrPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";
    private const string FrequencyValue = "SystemRestorePointCreationFrequency";
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;

    /// <summary>Verständlicher Fehlertext mit HRESULT, auch wenn Windows keine Meldung liefert.</summary>
    internal static string Describe(Exception ex)
    {
        var known = Explain(ex.HResult);
        if (known is not null)
        {
            return $"{known} (0x{ex.HResult:X8})";
        }

        var text = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message.Trim();
        return $"{text} (0x{ex.HResult:X8})";
    }

    /// <summary>Häufige Ursachen, in Alltagssprache.</summary>
    internal static string? Explain(int hresult) => unchecked((uint)hresult) switch
    {
        0x80070422 => "Der Windows-Dienst \"Volumeschattenkopie\" ist deaktiviert. Starttyp in den Diensten auf \"Manuell\" stellen",
        0x80070005 => "Zugriff verweigert. Die App braucht Administratorrechte",
        0x80042306 => "Der Schattenkopie-Anbieter meldet einen Fehler. Ein anderes Sicherungsprogramm blockiert eventuell",
        0x80042316 => "Gerade läuft schon eine andere Schattenkopie. Später erneut versuchen",
        0x8004230F => "Der Schattenkopie-Anbieter hat einen unerwarteten Fehler gemeldet",
        0x80070070 => "Zu wenig freier Speicherplatz für den Wiederherstellungspunkt",
        0x81000101 => "Windows erlaubt gerade keinen weiteren Wiederherstellungspunkt",
        0x80070032 => "Wiederherstellungspunkte werden auf diesem System nicht unterstützt",
        _ => null,
    };

    private static string SystemDrive => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";

    public ProtectionState GetProtectionState()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var policy = hklm.OpenSubKey(SrPolicyKey))
            {
                if (policy?.GetValue("DisableSR") is int disabled && disabled == 1)
                {
                    return ProtectionState.BlockedByPolicy;
                }
            }

            // RPSessionInterval = 1 heißt: Systemschutz ist für mindestens ein Laufwerk aktiv
            using var sr = hklm.OpenSubKey(SrKey);
            return sr?.GetValue("RPSessionInterval") is int interval && interval >= 1
                ? ProtectionState.Enabled
                : ProtectionState.Disabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return ProtectionState.Unknown;
        }
    }

    public OperationResult EnableProtection()
    {
        try
        {
            using var sr = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            using var input = sr.GetMethodParameters("Enable");
            input["Drive"] = SystemDrive;
            using var output = sr.InvokeMethod("Enable", input, null);
            var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u, CultureInfo.InvariantCulture);
            return code == 0 ? OperationResult.Ok : OperationResult.Fail(Explain(unchecked((int)code)) ?? $"Systemschutz einschalten: Code {code}");
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return OperationResult.Fail(Describe(ex));
        }
    }

    public OperationResult Create(string description)
    {
        // Windows erlaubt standardmäßig nur einen Punkt pro 24 Stunden: Sperre kurz aussetzen
        RegistryKey sr;
        object? previous;
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            sr = hklm.CreateSubKey(SrKey, writable: true);
            previous = sr.GetValue(FrequencyValue);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return OperationResult.Fail(Describe(ex));
        }

        using var _ = sr;
        try
        {
            sr.SetValue(FrequencyValue, 0, RegistryValueKind.DWord);

            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            using var input = cls.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = ModifySettings;
            input["EventType"] = BeginSystemChange;
            using var output = cls.InvokeMethod("CreateRestorePoint", input, null);
            var code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u, CultureInfo.InvariantCulture);
            return code == 0 ? OperationResult.Ok : OperationResult.Fail(Explain(unchecked((int)code)) ?? $"Wiederherstellungspunkt: Code {code}");
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return OperationResult.Fail(Describe(ex));
        }
        finally
        {
            if (previous is null)
            {
                sr.DeleteValue(FrequencyValue, throwOnMissingValue: false);
            }
            else
            {
                sr.SetValue(FrequencyValue, previous, RegistryValueKind.DWord);
            }
        }
    }

    public IReadOnlyList<RestorePointInfo> List()
    {
        var result = new List<RestorePointInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, Description, CreationTime FROM SystemRestore");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var created = ManagementDateTimeConverter.ToDateTime((string)item["CreationTime"]);
                    result.Add(new RestorePointInfo(
                        Convert.ToInt32(item["SequenceNumber"], CultureInfo.InvariantCulture),
                        (string?)item["Description"] ?? string.Empty,
                        created));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Systemschutz aus oder WMI nicht erreichbar: leere Liste
        }

        return result.OrderByDescending(r => r.Created).ToList();
    }
}
