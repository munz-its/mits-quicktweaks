using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Win32;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

/// <summary>DNS pro Adapter, Netzwerk zurücksetzen und Ping. DNS läuft über die Windows-Cmdlets, die auch die Einstellungen nutzen.</summary>
public static class WindowsNetwork
{
    private const string Tcpip4 = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
    private const string Tcpip6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\";

    /// <summary>Verbundene physische Adapter (LAN, WLAN) mit fest eingestellten DNS-Servern.</summary>
    public static IReadOnlyList<NetAdapterDns> ListAdapters()
    {
        var json = PowerShell("ConvertTo-Json -Compress -InputObject @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object ifIndex, Name, InterfaceGuid)");
        var list = new List<NetAdapterDns>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return list;
        }

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var guid = item.GetProperty("InterfaceGuid").GetString() ?? string.Empty;
            var servers = ReadServers(Tcpip4 + guid).Concat(ReadServers(Tcpip6 + guid)).ToList();
            list.Add(new NetAdapterDns(item.GetProperty("ifIndex").GetInt32(), item.GetProperty("Name").GetString() ?? "?", guid, servers));
        }

        return list;
    }

    /// <summary>Leere Liste = automatisch vom Router.</summary>
    public static OperationResult SetDns(NetAdapterDns adapter, IReadOnlyList<string> servers)
    {
        if (servers.Any(s => !IPAddress.TryParse(s, out _)))
        {
            return OperationResult.Fail("Ungültige DNS-Adresse.");
        }

        var command = servers.Count == 0
            ? $"Set-DnsClientServerAddress -InterfaceIndex {adapter.Index} -ResetServerAddresses -ErrorAction Stop"
            : $"Set-DnsClientServerAddress -InterfaceIndex {adapter.Index} -ServerAddresses ({string.Join(",", servers.Select(s => $"'{s}'"))}) -ErrorAction Stop";
        var (code, error) = PowerShellResult(command);
        return code == 0 ? OperationResult.Ok : OperationResult.Fail(string.IsNullOrWhiteSpace(error) ? $"Code {code}" : error.Trim());
    }

    /// <summary>Winsock und TCP/IP auf Werkszustand. Wirkt nach einem Neustart.</summary>
    public static OperationResult ResetStack()
    {
        foreach (var args in new[] { "winsock reset", "int ip reset" })
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return OperationResult.Fail("netsh ließ sich nicht starten.");
            }

            process.StandardOutput.ReadToEnd();
            process.WaitForExit(60_000);
        }

        return OperationResult.Ok;
    }

    public static async Task<PingResult> PingAsync(PingTarget target, int count = 4)
    {
        using var ping = new Ping();
        var times = new List<long>();
        var lost = 0;
        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(target.Host, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    times.Add(reply.RoundtripTime);
                }
                else
                {
                    lost++;
                }
            }
            catch (PingException)
            {
                lost++;
            }
        }

        return new PingResult(target.Name, target.Host, times.Count > 0 ? Math.Round(times.Average(), 1) : null, lost, count);
    }

    private static IEnumerable<string> ReadServers(string key)
    {
        using var sub = Registry.LocalMachine.OpenSubKey(key);
        var raw = sub?.GetValue("NameServer") as string ?? string.Empty;
        return raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string PowerShell(string command)
    {
        var (_, output, _) = Run(command);
        return output;
    }

    private static (int Code, string Error) PowerShellResult(string command)
    {
        var (code, _, error) = Run(command);
        return (code, error);
    }

    private static (int Code, string Output, string Error) Run(string command)
    {
        var exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = Process.Start(new ProcessStartInfo(exe)
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("PowerShell ließ sich nicht starten.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PowerShell hat nicht geantwortet.");
        }

        return (process.ExitCode, output.Result, error.Result);
    }
}
