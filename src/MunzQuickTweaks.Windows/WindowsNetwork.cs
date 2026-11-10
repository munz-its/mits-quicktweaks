using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Win32;
using MunzQuickTweaks.Core.Safety;
using MunzQuickTweaks.Core.Tools;

namespace MunzQuickTweaks.Windows;

/// <summary>DNS pro Adapter, sichere Netzwerk-Reparatur und Ping. Bewusst kein globaler netsh-Reset von Winsock oder TCP/IP: der kann WLAN ohne Adresse zurücklassen. DNS läuft über die Windows-Cmdlets, die auch die Einstellungen nutzen.</summary>
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
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var guid = item.GetProperty("InterfaceGuid").GetString() ?? string.Empty;
            var servers = ReadServers(Tcpip4 + guid).Concat(ReadServers(Tcpip6 + guid)).ToList();
            list.Add(new NetAdapterDns(item.GetProperty("ifIndex").GetInt32(), item.GetProperty("Name").GetString() ?? "?", guid, servers)
            {
                Ipv4 = Ipv4Of(interfaces, guid),
            });
        }

        return list;
    }

    private static IReadOnlyList<string> Ipv4Of(NetworkInterface[] interfaces, string guid)
    {
        var nic = interfaces.FirstOrDefault(n => string.Equals(n.Id, guid, StringComparison.OrdinalIgnoreCase));
        return nic is null
            ? Array.Empty<string>()
            : nic.GetIPProperties().UnicastAddresses
                .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(u => u.Address.ToString())
                .ToList();
    }

    /// <summary>Klappt eine DNS-Auflösung gerade? Prüft zwei Namen, damit ein einzelner Ausfall nicht täuscht.</summary>
    public static async Task<bool> CanResolveAsync()
    {
        foreach (var host in new[] { "www.microsoft.com", "munz-its.de" })
        {
            try
            {
                var lookup = Dns.GetHostAddressesAsync(host);
                if (await Task.WhenAny(lookup, Task.Delay(4000)) == lookup && lookup.Result.Length > 0)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
            {
                // nächster Name
            }
        }

        return false;
    }

    /// <summary>
    /// Sichere Reparatur ohne Neustart. Setzt nichts global zurück: Nur Adapter ohne gültige Adresse
    /// bekommen wieder DHCP und automatische DNS-Server. Fest eingestellte Adressen bleiben unangetastet.
    /// </summary>
    public static OperationResult Repair()
    {
        const string script = """
            $ErrorActionPreference = 'Continue'
            Set-Service Dhcp -StartupType Automatic
            Start-Service Dhcp
            $fixed = @()
            foreach ($a in Get-NetAdapter -Physical | Where-Object Status -eq 'Up') {
                $ips = @(Get-NetIPAddress -InterfaceIndex $a.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '169.254.*' })
                if ($ips.Count -eq 0) {
                    Set-NetIPInterface -InterfaceIndex $a.ifIndex -AddressFamily IPv4 -Dhcp Enabled
                    Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ResetServerAddresses
                    Restart-NetAdapter -Name $a.Name -Confirm:$false
                    $fixed += $a.Name
                }
            }
            if ($fixed.Count -gt 0) { Start-Sleep -Seconds 8; ipconfig /renew | Out-Null }
            ipconfig /flushdns | Out-Null
            Write-Output ($fixed -join ', ')
            """;
        var (code, output, error) = Run(script);
        return code == 0 ? OperationResult.Ok : OperationResult.Fail(string.IsNullOrWhiteSpace(error) ? $"Code {code}" : error.Trim());
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
