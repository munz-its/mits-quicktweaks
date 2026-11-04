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
            return code == 0 ? OperationResult.Ok : OperationResult.Fail($"Enable: Code {code}");
        }
        catch (ManagementException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public OperationResult Create(string description)
    {
        // Windows erlaubt standardmäßig nur einen Punkt pro 24 Stunden: Sperre kurz aussetzen
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var sr = hklm.CreateSubKey(SrKey, writable: true);
        var previous = sr.GetValue(FrequencyValue);

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
            return code == 0 ? OperationResult.Ok : OperationResult.Fail($"CreateRestorePoint: Code {code}");
        }
        catch (ManagementException ex)
        {
            return OperationResult.Fail(ex.Message);
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
        catch (ManagementException)
        {
            // Systemschutz aus oder WMI nicht erreichbar: leere Liste
        }

        return result.OrderByDescending(r => r.Created).ToList();
    }
}
