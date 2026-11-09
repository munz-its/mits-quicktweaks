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

    public IReadOnlyList<RestorablePackage> ListRestorable()
    {
        var wanted = new HashSet<string>(DebloatCatalog.All.Select(a => a.Package), StringComparer.OrdinalIgnoreCase);
        var mine = new HashSet<string>(ListInstalled().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, RestorablePackage>(StringComparer.OrdinalIgnoreCase);

        void Collect(Func<IEnumerable<global::Windows.ApplicationModel.Package>> source)
        {
            try
            {
                foreach (var package in source())
                {
                    var name = package.Id.Name;
                    if (wanted.Contains(name) && !mine.Contains(name) && !package.IsFramework && !package.IsResourcePackage)
                    {
                        found.TryAdd(name, new RestorablePackage(name, package.Id.FamilyName));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                // Quelle nicht lesbar: ohne sie weiter
            }
        }

        // Vorinstallierte Kopie von Windows und Installationen anderer Konten
        Collect(() => _manager.FindProvisionedPackages());
        Collect(() => _manager.FindPackages());
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<OperationResult> ReinstallAsync(string familyName)
    {
        try
        {
            var result = await _manager.RegisterPackageByFamilyNameAsync(familyName, null, DeploymentOptions.None, null, null).AsTask();
            return result.IsRegistered ? OperationResult.Ok : OperationResult.Fail(result.ErrorText);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public long? GetSize(string packageFullName)
    {
        try
        {
            var path = _manager.FindPackageForUser(string.Empty, packageFullName)?.InstalledPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return null;
            }

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return new DirectoryInfo(path).EnumerateFiles("*", options).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return null;
        }
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
