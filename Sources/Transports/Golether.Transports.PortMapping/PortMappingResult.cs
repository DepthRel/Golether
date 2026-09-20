using System.Net;

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
