using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.PortMapping;

/// <summary>
/// NAT Port Mapping Protocol (RFC 6886): the router listens on UDP 5351 of the default gateway.
/// </summary>
public sealed class NatPmpClient : IPortMappingProtocol
{
    /// <summary>
    /// The NAT-PMP port of the gateway.
    /// </summary>
    public const int DefaultPort = 5351;

    /// <summary>
    /// Opcode: public address request.
    /// </summary>
    private const byte ExternalAddressOpcode = 0;

    /// <summary>
    /// Opcode: TCP mapping request.
    /// </summary>
    private const byte MapTcpOpcode = 2;

    /// <summary>
    /// The offset added to the opcode in responses.
    /// </summary>
    private const byte ResponseFlag = 128;

    /// <summary>
    /// The gateways to ask.
    /// </summary>
    private readonly Func<IReadOnlyList<IPEndPoint>> _gateways;

    /// <summary>
    /// The first retransmission timeout; it doubles with each attempt (RFC 6886 starts at 250 ms).
    /// </summary>
    private readonly TimeSpan _initialTimeout;

    /// <summary>
    /// The number of attempts per request.
    /// </summary>
    private readonly int _attempts;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The gateway that answered the last mapping.
    /// </summary>
    private IPEndPoint? _gateway;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatPmpClient"/> class.
    /// </summary>
    /// <param name="gateways">The gateways to ask; the default gateways of this device when <see langword="null"/>.</param>
    /// <param name="initialTimeout">The first retransmission timeout (250 ms when <see langword="null"/>).</param>
    /// <param name="attempts">The number of attempts per request.</param>
    /// <param name="logger">The logger.</param>
    public NatPmpClient(Func<IReadOnlyList<IPEndPoint>>? gateways = null, TimeSpan? initialTimeout = null, int attempts = 3, ILogger<NatPmpClient>? logger = null)
    {
        _gateways = gateways ?? (() => NetworkAddresses.GetGateways().Select(g => new IPEndPoint(g, DefaultPort)).ToArray());
        _initialTimeout = initialTimeout ?? TimeSpan.FromMilliseconds(250);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        _attempts = attempts;
        _logger = logger ?? NullLogger<NatPmpClient>.Instance;
    }

    /// <inheritdoc />
    public string Name => "NAT-PMP";

    /// <inheritdoc />
    public async Task<PortMappingResult?> MapAsync(int port, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        foreach (var gateway in _gateways())
        {
            var mapping = await RequestMappingAsync(gateway, port, port, (uint)Math.Clamp(lifetime.TotalSeconds, 1, uint.MaxValue), cancellationToken).ConfigureAwait(false);
            if (mapping is null)
            {
                continue;
            }

            _gateway = gateway;
            var address = await RequestExternalAddressAsync(gateway, cancellationToken).ConfigureAwait(false);
            return new PortMappingResult(Name, port, mapping.Value.ExternalPort, address, TimeSpan.FromSeconds(mapping.Value.Lifetime));
        }

        return null;
    }

    /// <inheritdoc />
    public async Task UnmapAsync(PortMappingResult mapping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (_gateway is { } gateway)
        {
            // A lifetime of zero deletes the mapping; the suggested external port must be zero as well.
            await RequestMappingAsync(gateway, mapping.InternalPort, 0, 0, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds a mapping request.
    /// </summary>
    /// <param name="internalPort">The port on this device.</param>
    /// <param name="externalPort">The suggested port on the router.</param>
    /// <param name="lifetime">The lease in seconds; zero deletes the mapping.</param>
    /// <returns>The 12-byte request.</returns>
    internal static byte[] BuildMappingRequest(int internalPort, int externalPort, uint lifetime)
    {
        var request = new byte[12];
        request[1] = MapTcpOpcode;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), (ushort)externalPort);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), lifetime);
        return request;
    }

    /// <summary>
    /// Parses a mapping response.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="internalPort">The port the request was for.</param>
    /// <returns>The external port and lease, or <see langword="null"/> when the router refused or the data is invalid.</returns>
    internal static (int ExternalPort, uint Lifetime)? ParseMappingResponse(ReadOnlySpan<byte> response, int internalPort)
    {
        if (response.Length < 16 || response[0] != 0 || response[1] != MapTcpOpcode + ResponseFlag
            || BinaryPrimitives.ReadUInt16BigEndian(response[2..]) != 0
            || BinaryPrimitives.ReadUInt16BigEndian(response[8..]) != internalPort)
        {
            return null;
        }

        return (BinaryPrimitives.ReadUInt16BigEndian(response[10..]), BinaryPrimitives.ReadUInt32BigEndian(response[12..]));
    }

    /// <summary>
    /// Parses a public address response.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The address, or <see langword="null"/> when the data is invalid.</returns>
    internal static IPAddress? ParseExternalAddressResponse(ReadOnlySpan<byte> response)
        => response.Length >= 12 && response[0] == 0 && response[1] == ExternalAddressOpcode + ResponseFlag
           && BinaryPrimitives.ReadUInt16BigEndian(response[2..]) == 0
            ? new IPAddress(response.Slice(8, 4))
            : null;

    /// <summary>
    /// Sends a mapping request.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="internalPort">The port on this device.</param>
    /// <param name="externalPort">The suggested port on the router.</param>
    /// <param name="lifetime">The lease in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapping, or <see langword="null"/>.</returns>
    private async Task<(int ExternalPort, uint Lifetime)?> RequestMappingAsync(IPEndPoint gateway, int internalPort, int externalPort, uint lifetime, CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync(gateway, BuildMappingRequest(internalPort, externalPort, lifetime), MapTcpOpcode, cancellationToken).ConfigureAwait(false);
        return response is null ? null : ParseMappingResponse(response, internalPort);
    }

    /// <summary>
    /// Asks for the public address.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private async Task<IPAddress?> RequestExternalAddressAsync(IPEndPoint gateway, CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync(gateway, [0, ExternalAddressOpcode], ExternalAddressOpcode, cancellationToken).ConfigureAwait(false);
        return response is null ? null : ParseExternalAddressResponse(response);
    }

    /// <summary>
    /// Sends a request with retransmissions and waits for the matching response from the gateway.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="request">The request.</param>
    /// <param name="opcode">The request opcode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response, or <see langword="null"/> when the gateway did not answer.</returns>
    private async Task<byte[]?> ExchangeAsync(IPEndPoint gateway, byte[] request, byte opcode, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(gateway.AddressFamily);
        var timeout = _initialTimeout;
        for (var attempt = 0; attempt < _attempts; attempt++)
        {
            await udp.SendAsync(request, gateway, cancellationToken).ConfigureAwait(false);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    var received = await udp.ReceiveAsync(wait.Token).ConfigureAwait(false);

                    // Only the gateway itself may answer (RFC 6886, section 3.1).
                    if (received.RemoteEndPoint.Address.Equals(gateway.Address) && received.Buffer is [0, var op, ..] && op == opcode + ResponseFlag)
                    {
                        return received.Buffer;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timeout += timeout;
            }
            catch (SocketException ex)
            {
                // ICMP "port unreachable": the router does not speak NAT-PMP.
                _logger.LogDebug("NAT-PMP at {Gateway}: {Error}", gateway, ex.SocketErrorCode);
                return null;
            }
        }

        return null;
    }
}
