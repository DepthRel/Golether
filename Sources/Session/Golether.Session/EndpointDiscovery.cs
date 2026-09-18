using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Golether.Core.Networking;

namespace Golether.Session;

/// <summary>
/// Finds the addresses this device can be reached at.
/// </summary>
public static class EndpointDiscovery
{
    /// <summary>
    /// Lists the addresses of active network interfaces: tunnel interfaces first (AmneziaWG, WireGuard), then private
    /// and public IPv4, then global IPv6. Loopback and link-local addresses are skipped.
    /// </summary>
    /// <param name="port">The listening port.</param>
    /// <param name="additional">Addresses configured by the user (for example a forwarded public address), listed first.</param>
    /// <returns>The candidate endpoints without duplicates.</returns>
    public static IReadOnlyList<PeerEndpoint> Discover(int port, IEnumerable<PeerEndpoint>? additional = null)
    {
        var result = new List<PeerEndpoint>();
        if (additional is not null)
        {
            result.AddRange(additional);
        }

        var candidates = new List<(int Rank, string Address)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var isTunnel = IsTunnel(nic);
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
                    || (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes() is [169, 254, ..]))
                {
                    continue;
                }

                var rank = isTunnel ? 0 : address.AddressFamily == AddressFamily.InterNetwork ? 1 : 2;
                candidates.Add((rank, address.ToString()));
            }
        }

        result.AddRange(candidates.OrderBy(c => c.Rank).Select(c => new PeerEndpoint(StripScope(c.Address), port)));
        return result.Distinct().Take(12).ToArray();
    }

    /// <summary>
    /// Lists the addresses this device has on tunnel networks (AmneziaWG, WireGuard, a tunnel of Golether itself).
    /// A participant on the same tunnel reaches these addresses whatever the router does with the port.
    /// </summary>
    /// <returns>The addresses, empty when no tunnel is up.</returns>
    public static IReadOnlyList<string> DiscoverTunnelAddresses()
        => [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up && IsTunnel(nic))
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal)
            .Select(address => StripScope(address.ToString()))
            .Distinct()];

    /// <summary>
    /// Checks whether an interface is a tunnel.
    /// </summary>
    /// <param name="nic">The interface.</param>
    /// <returns><see langword="true"/> for a tunnel interface.</returns>
    private static bool IsTunnel(NetworkInterface nic)
        => nic.Name.Contains("golether", StringComparison.OrdinalIgnoreCase)
           || nic.Description.Contains("AmneziaWG", StringComparison.OrdinalIgnoreCase)
           || nic.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the IPv6 scope suffix.
    /// </summary>
    /// <param name="address">The address text.</param>
    /// <returns>The address without <c>%scope</c>.</returns>
    private static string StripScope(string address)
    {
        var percent = address.IndexOf('%');
        return percent < 0 ? address : address[..percent];
    }
}
