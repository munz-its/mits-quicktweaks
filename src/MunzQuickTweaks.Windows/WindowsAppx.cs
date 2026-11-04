using MunzQuickTweaks.Core.Modules;
using MunzQuickTweaks.Core.Safety;
using Windows.Management.Deployment;

namespace MunzQuickTweaks.Windows;

/// <summary>Store-Apps des angemeldeten Nutzers über die Windows-Paketverwaltung.</summary>
public sealed class WindowsAppx : IAppxManager
{
    private readonly PackageManager _manager = new();

    public IReadOnlyList<InstalledPackage> ListInstalled()
    {
        // Leerer Benutzer-SID = aktueller Nutzer
        return _manager.FindPackagesForUser(string.Empty)
            .Where(p => !p.IsFramework && !p.IsResourcePackage)
            .Select(p => new InstalledPackage(p.Id.Name, p.Id.FullName))
            .ToList();
    }

    public async Task<OperationResult> RemoveAsync(string packageFullName)
    {
        try
        {
            var result = await _manager.RemovePackageAsync(packageFullName).AsTask();
            return result.IsRegistered
                ? OperationResult.Fail(result.ErrorText)
                : OperationResult.Ok;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            return OperationResult.Fail(ex.Message);
        }
    }
}
