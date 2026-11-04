using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MunzQuickTweaks.Windows;

/// <summary>
/// Erkennt, ob die App unter einem anderen Konto läuft als dem angemeldeten Nutzer
/// (Standard-Nutzer hat beim Start ein fremdes Admin-Passwort eingegeben).
/// </summary>
public static partial class AccountInfo
{
    private static readonly IntPtr CurrentServer = IntPtr.Zero;
    private const int CurrentSession = -1;
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    /// <summary>Konto, unter dem die App läuft, z. B. PC\Mert.</summary>
    public static string ProcessAccount
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    /// <summary>Angemeldeter Nutzer der Sitzung. Null, wenn nicht ermittelbar.</summary>
    public static string? SessionAccount
    {
        get
        {
            var user = Query(WtsUserName);
            var domain = Query(WtsDomainName);
            return string.IsNullOrEmpty(user) ? null : string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
        }
    }

    /// <summary>Im Zweifel false: lieber nichts sperren als fälschlich sperren.</summary>
    public static bool IsForeignElevation()
    {
        var session = SessionAccount;
        return session is not null && !string.Equals(session, ProcessAccount, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Query(int infoClass)
    {
        if (!WTSQuerySessionInformationW(CurrentServer, CurrentSession, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr memory);
}
