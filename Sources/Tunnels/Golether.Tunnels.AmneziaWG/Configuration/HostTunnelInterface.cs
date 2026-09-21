using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Golether.Localization;
using Golether.Tunnels.AmneziaWG.Keys;

namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// The AmneziaWG interface of the host: one interface, one <c>/24</c> subnet and one peer per participant.
/// </summary>
/// <remarks>
/// Participants get addresses <c>.2</c>–<c>.254</c>; each participant only reaches the host (<c>AllowedIPs</c> is the
/// host <c>/32</c>), traffic between participants is relayed by the application.
/// </remarks>
public sealed record HostTunnelInterface
{
    /// <summary>
    /// The prefix length of the tunnel subnet.
    /// </summary>
    public const int SubnetPrefix = 24;

    /// <summary>
    /// The default keepalive interval in seconds; it also keeps NAT mappings open.
    /// </summary>
    public const int KeepaliveSeconds = 25;

    /// <summary>
    /// Gets the interface name (at most 15 characters), for example <c>golether0</c>.
    /// </summary>
    public required string InterfaceName { get; init; }

    /// <summary>
    /// Gets the key pair.
    /// </summary>
    public required AwgKeyPair Keys { get; init; }

    /// <summary>
    /// Gets the UDP port.
    /// </summary>
    public required int ListenPort { get; init; }

    /// <summary>
    /// Gets the subnet base address, for example <c>10.77.41.0</c>.
    /// </summary>
    public required string SubnetBase { get; init; }

    /// <summary>
    /// Gets the junk parameters of the host.
    /// </summary>
    public required JunkParameters Junk { get; init; }

    /// <summary>
    /// Gets the shared obfuscation parameters.
    /// </summary>
    public required SharedObfuscation Obfuscation { get; init; }

    /// <summary>
    /// Gets the host tunnel address, for example <c>10.77.41.1</c>.
    /// </summary>
    public string HostAddress => AddressAt(1);

    /// <summary>
    /// Creates an interface with random keys, port, subnet and obfuscation.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>The interface.</returns>
    public static HostTunnelInterface Create(string interfaceName = "golether0")
    {
        TunnelNames.Validate(interfaceName);

        // 10.64.0.0 – 10.127.255.0: rarely used by home routers and corporate networks.
        var second = RandomNumberGenerator.GetInt32(64, 128);
        var third = RandomNumberGenerator.GetInt32(0, 256);
        return new HostTunnelInterface
        {
            InterfaceName = interfaceName,
            Keys = AwgKeys.Generate(),
            ListenPort = PickListenPort(),
            SubnetBase = $"10.{second}.{third}.0",
            Junk = JunkParameters.Generate(),
            Obfuscation = SharedObfuscation.Generate(),
        };
    }

    /// <summary>
    /// Picks a UDP port that is free right now. The range overlaps the ports Windows hands out to any program that
    /// asks for one, so a port taken at this moment would make the tunnel fail to start and its public address
    /// impossible to look up.
    /// </summary>
    /// <returns>The port.</returns>
    public static int PickListenPort()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var port = RandomNumberGenerator.GetInt32(40000, 60000);
            if (IsFree(port))
            {
                return port;
            }
        }

        // Every candidate was busy: take one anyway rather than fail here.
        return RandomNumberGenerator.GetInt32(40000, 60000);
    }

    /// <summary>
    /// Checks whether a UDP port can be taken.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns><see langword="true"/> when the port is free.</returns>
    private static bool IsFree(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the address with a host number in the subnet.
    /// </summary>
    /// <param name="hostNumber">1–254.</param>
    /// <returns>The address.</returns>
    public string AddressAt(int hostNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hostNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hostNumber, 254);
        var bytes = IPAddress.Parse(SubnetBase).GetAddressBytes();
        bytes[3] = (byte)hostNumber;
        return new IPAddress(bytes).ToString();
    }

    /// <summary>
    /// Returns the first participant address not in use.
    /// </summary>
    /// <param name="used">The addresses already assigned.</param>
    /// <returns>The address.</returns>
    /// <exception cref="InvalidOperationException">The subnet is exhausted.</exception>
    public string NextParticipantAddress(IReadOnlyCollection<string> used)
    {
        for (var number = 2; number <= 254; number++)
        {
            var candidate = AddressAt(number);
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(Texts.Get("Tunnel.Error.SubnetFull"));
    }

    /// <summary>
    /// Builds the host configuration with the given peers.
    /// </summary>
    /// <param name="peers">The participant peers.</param>
    /// <returns>The configuration.</returns>
    public AwgConfiguration BuildConfiguration(IReadOnlyList<AwgPeer> peers)
        => new(
            new AwgInterface
            {
                PrivateKey = Keys.PrivateKey,
                Address = $"{HostAddress}/{SubnetPrefix}",
                ListenPort = ListenPort,
                Junk = Junk,
                Obfuscation = Obfuscation,
            },
            peers);
}
