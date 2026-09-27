using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Golether.Transports.Rendezvous.Protocol;

namespace Golether.Transports.Rendezvous.Punching;

/// <summary>
/// Lists the local IPv4 addresses of this device, for two devices behind one router that cannot reach each other
/// through its public address.
/// </summary>
public static class LocalAddresses
{
    /// <summary>
    /// Lists the addresses of the active interfaces with a port, as texts for a message.
    /// </summary>
    /// <param name="port">The local port.</param>
    /// <returns>At most <see cref="RendezvousProtocol.MaxLocals"/> addresses; empty when the system does not tell.</returns>
    public static string[] Describe(int port)
    {
        try
        {
            return
            [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                    .Select(unicast => unicast.Address)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)
                        && address.GetAddressBytes() is not [169, 254, ..])
                    .Distinct()
                    .Take(RendezvousProtocol.MaxLocals)
                    .Select(address => RendezvousProtocol.Format(new IPEndPoint(address, port))),
            ];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
