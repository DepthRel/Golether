using System.Net;

namespace Golether.Transports.Relay;

/// <summary>
/// The address a device has on the public internet, as a STUN server sees it.
/// </summary>
/// <param name="Endpoint">The address and port the outside world reaches.</param>
/// <param name="LocalPort">The local port the answer belongs to.</param>
/// <param name="IsPredictable">
/// Whether the same local port looked the same from two different servers. When it did not, the router gives every
/// destination its own port (a symmetric NAT) and the address in an invitation is worthless.
/// </param>
public sealed record PublicEndpoint(IPEndPoint Endpoint, int LocalPort, bool IsPredictable);
