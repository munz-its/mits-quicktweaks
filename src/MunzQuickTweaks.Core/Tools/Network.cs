namespace MunzQuickTweaks.Core.Tools;

/// <summary>Öffentlicher DNS-Anbieter mit IPv4 und IPv6.</summary>
public sealed record DnsProvider(string Id, string Name, IReadOnlyList<string> Servers);

/// <summary>Aktiver Netzwerkadapter. Servers leer = automatisch vom Router.</summary>
public sealed record NetAdapterDns(int Index, string Name, string Guid, IReadOnlyList<string> Servers)
{
    /// <summary>IPv4-Adressen des Adapters. Leer oder nur 169.254.x.x heißt: keine Adresse vom Router bekommen.</summary>
    public IReadOnlyList<string> Ipv4 { get; init; } = Array.Empty<string>();

    public bool HasNoAddress => !NetworkHealth.HasValidIpv4(Ipv4);
}

public static class NetworkHealth
{
    /// <summary>Gültig ist jede IPv4 außer der Windows-Notadresse 169.254.x.x (kein DHCP).</summary>
    public static bool HasValidIpv4(IEnumerable<string> addresses) =>
        addresses.Any(a => System.Net.IPAddress.TryParse(a, out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && !a.StartsWith("169.254.", StringComparison.Ordinal)
            && !a.StartsWith("0.", StringComparison.Ordinal));
}

public sealed record PingTarget(string Name, string Host);

public sealed record PingResult(string Name, string Host, double? AverageMs, int Lost, int Sent);

public static class NetworkPresets
{
    public static readonly IReadOnlyList<DnsProvider> Providers = new[]
    {
        new DnsProvider("cloudflare", "Cloudflare", new[] { "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001" }),
        new DnsProvider("quad9", "Quad9", new[] { "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9" }),
        new DnsProvider("google", "Google", new[] { "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844" }),
    };

    public static readonly IReadOnlyList<PingTarget> PingTargets = new[]
    {
        new PingTarget("Cloudflare DNS", "1.1.1.1"),
        new PingTarget("Google DNS", "8.8.8.8"),
        new PingTarget("Steam", "store.steampowered.com"),
        new PingTarget("Epic Games", "www.epicgames.com"),
        new PingTarget("Discord", "discord.com"),
        new PingTarget("Battle.net", "eu.battle.net"),
        new PingTarget("Riot Games", "www.riotgames.com"),
    };

    /// <summary>Welcher Anbieter gerade eingestellt ist: Kennung, "auto" oder null für eigene Server.</summary>
    public static string? Detect(IReadOnlyList<string> servers)
    {
        if (servers.Count == 0)
        {
            return "auto";
        }

        return Providers.FirstOrDefault(p => servers.All(s => p.Servers.Contains(s, StringComparer.OrdinalIgnoreCase)))?.Id;
    }
}
