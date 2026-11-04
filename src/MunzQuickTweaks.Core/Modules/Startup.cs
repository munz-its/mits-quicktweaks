namespace MunzQuickTweaks.Core.Modules;

public enum StartupSource
{
    /// <summary>HKCU\...\Run</summary>
    UserRegistry,
    /// <summary>HKLM\...\Run</summary>
    MachineRegistry,
    /// <summary>HKLM\...\WOW6432Node\...\Run (32-Bit-Programme)</summary>
    MachineRegistry32,
    /// <summary>Autostart-Ordner des Nutzers</summary>
    UserFolder,
    /// <summary>Autostart-Ordner für alle Nutzer</summary>
    CommonFolder,
    /// <summary>Geplante Aufgabe mit Anmelde- oder Start-Auslöser</summary>
    ScheduledTask,
}

public sealed record StartupEntry(string Id, string Name, string Command, StartupSource Source, bool Enabled)
{
    public bool IsUserLevel => Source is StartupSource.UserRegistry or StartupSource.UserFolder;
}

public interface IStartupManager
{
    IReadOnlyList<StartupEntry> List();

    Safety.OperationResult SetEnabled(StartupEntry entry, bool enabled);
}

/// <summary>
/// Gleiches Verfahren wie der Task-Manager: Einträge werden nicht gelöscht, sondern im Schlüssel
/// StartupApproved als deaktiviert markiert. Dadurch ist jede Änderung verlustfrei umkehrbar.
/// </summary>
public static class StartupApproved
{
    /// <summary>12 Byte: 4 Byte Status, 8 Byte Zeitpunkt (FILETIME) der Deaktivierung.</summary>
    public const int Length = 12;

    /// <summary>Fehlt der Wert, gilt der Eintrag als aktiv. Gerade erste Bytes (02, 06) = aktiv, ungerade (03, 07) = deaktiviert.</summary>
    public static bool IsEnabled(byte[]? data) => data is null || data.Length == 0 || (data[0] & 1) == 0;

    public static byte[] Create(bool enabled, DateTime nowUtc)
    {
        var data = new byte[Length];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.GetBytes(nowUtc.ToFileTimeUtc()).CopyTo(data, 4);
        }

        return data;
    }

    /// <summary>Lesbarer Programmpfad aus einer Befehlszeile, z. B. "C:\Program Files\X\x.exe" --tray.</summary>
    public static string ExtractPath(string command)
    {
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text.Trim('"');
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? text[..(exe + 4)] : text.Split(' ')[0];
    }
}
