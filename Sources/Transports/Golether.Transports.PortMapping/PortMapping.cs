using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Golether.Transports.PortMapping;

/// <summary>
/// A TCP port opened on the router.
/// </summary>
/// <param name="Method">How the port was opened: <c>NAT-PMP</c> or <c>UPnP</c>.</param>
/// <param name="InternalPort">The port on this device.</param>
/// <param name="ExternalPort">The port on the router.</param>
/// <param name="ExternalAddress">The public address of the router, or <see langword="null"/> when unknown.</param>
/// <param name="Lifetime">The lease the router granted; <see cref="TimeSpan.Zero"/> for a permanent mapping.</param>
public sealed record PortMappingResult(string Method, int InternalPort, int ExternalPort, IPAddress? ExternalAddress, TimeSpan Lifetime)
{
    /// <summary>
    /// Gets a value indicating whether the external address can be reached from the Internet (it is not a private,
    /// shared (carrier-grade NAT) or loopback address).
    /// </summary>
    public bool IsPubliclyReachable => ExternalAddress is not null && !NetworkAddresses.IsNonPublic(ExternalAddress);
}

/// <summary>
/// Opens and closes a port on one kind of router interface.
/// </summary>
public interface IPortMappingProtocol
{
    /// <summary>
    /// Gets the name of the protocol.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Opens a TCP port.
    /// </summary>
    /// <param name="port">The port on this device, also requested on the router.</param>
    /// <param name="lifetime">The requested lease.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapping, or <see langword="null"/> when the router does not answer or refuses.</returns>
    Task<PortMappingResult?> MapAsync(int port, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// Closes a mapping opened by <see cref="MapAsync"/>.
    /// </summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the router answered or gave up.</returns>
    Task UnmapAsync(PortMappingResult mapping, CancellationToken cancellationToken);
}

/// <summary>
/// Address helpers for port mapping.
/// </summary>
public static class NetworkAddresses
{
    /// <summary>
    /// Checks whether an IPv4 address cannot be reached from the Internet: private (RFC 1918), shared carrier-grade
    /// NAT (RFC 6598), link-local, loopback or unspecified.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> for a non-public address.</returns>
    public static bool IsNonPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var b = address.GetAddressBytes();
        return b[0] is 0 or 10 or 127
               || (b[0] == 172 && b[1] is >= 16 and <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)
               || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    /// <summary>
    /// Checks whether an address belongs to the local network (a router may be contacted there).
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> for private, link-local and loopback addresses.</returns>
    public static bool IsLocalNetwork(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return IPAddress.IsLoopback(address);
        }

        var b = address.GetAddressBytes();
        return b[0] is 10 or 127
               || (b[0] == 172 && b[1] is >= 16 and <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>
    /// Lists the IPv4 default gateways of active interfaces.
    /// </summary>
    /// <returns>The gateways, without duplicates.</returns>
    public static IReadOnlyList<IPAddress> GetGateways()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any) && IsLocalNetwork(a))
            .Distinct()
            .ToArray();

    /// <summary>
    /// Returns the local address this device uses to reach a host.
    /// </summary>
    /// <param name="remote">The host.</param>
    /// <returns>The local address.</returns>
    public static IPAddress GetLocalAddressFor(IPAddress remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        using var socket = new Socket(remote.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        // Connecting a UDP socket sends nothing; it only selects the route and the source address.
        socket.Connect(remote, 9);
        return ((IPEndPoint)socket.LocalEndPoint!).Address;
    }
}
