using System.Net;

namespace Golether.Transports.PortMapping;

/// <summary>
/// A port forwarding service of a router.
/// </summary>
/// <param name="ServiceType">The UPnP service type.</param>
/// <param name="ControlUri">The SOAP control address.</param>
/// <param name="Device">The address of the router.</param>
internal sealed record GatewayService(string ServiceType, Uri ControlUri, IPAddress Device);
