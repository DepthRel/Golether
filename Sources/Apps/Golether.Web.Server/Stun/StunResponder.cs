using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Golether.Localization;
using Golether.Transports.Relay;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;
using StunConstants = Golether.Transports.Relay.Stun;

namespace Golether.Web.Server.Stun;

/// <summary>
/// Answers STUN binding requests (RFC 5389) on the UDP port: tells a device which public address its UDP port has.
/// The cameras and voices of a session use it to reach each other directly; nothing else is served.
/// </summary>
/// <remarks>
/// The answer is barely larger than the request and is sent only to the address the request came from, within a
/// per-address limit, so the responder is useless for amplifying traffic towards somebody else.
/// </remarks>
internal sealed class StunResponder : BackgroundService
{
    /// <summary>
    /// The largest request answered; a binding request of a browser or of libnice is well under it.
    /// </summary>
    private const int MaxRequestSize = 548;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly RendezvousServerOptions _options;

    /// <summary>
    /// The counters.
    /// </summary>
    private readonly ServerStatistics _statistics;

    /// <summary>
    /// Limits the requests per address.
    /// </summary>
    private readonly AddressRateLimiter _limiter;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<StunResponder> _logger;

    /// <summary>
    /// The journal of events for the monitor, or <see langword="null"/> without one.
    /// </summary>
    private readonly ServerJournal? _journal;

    /// <summary>
    /// The UDP socket, or <see langword="null"/> before the start.
    /// </summary>
    private Socket? _socket;

    /// <summary>
    /// Initializes a new instance of the <see cref="StunResponder"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="statistics">The counters.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="journal">The journal of events for the monitor, or <see langword="null"/> without one.</param>
    public StunResponder(RendezvousServerOptions options, ServerStatistics statistics, TimeProvider timeProvider, ILogger<StunResponder> logger, ServerJournal? journal = null)
    {
        _journal = journal;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _limiter = new AddressRateLimiter(options.MaxStunPerMinute, timeProvider);
    }

    /// <summary>
    /// Gets the address the responder listens on.
    /// </summary>
    public IPEndPoint? LocalEndpoint => _socket?.LocalEndPoint as IPEndPoint;

    /// <summary>
    /// Opens the UDP port before the host reports the start.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the loop runs.</returns>
    /// <exception cref="InvalidOperationException">The port is busy.</exception>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(_options.ParsedListenAddress, _options.Port);
        var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (endpoint.Address.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true;
            }

            socket.Bind(endpoint);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw new InvalidOperationException(Texts.Format("Server.Error.PortBusy", "udp://" + endpoint, ex.Message), ex);
        }

        _socket = socket;
        _logger.LogInformation("STUN answering on udp://{Endpoint}", socket.LocalEndPoint);
        _journal?.Record(Contracts.JournalEvent.StunListening, detail: "udp://" + socket.LocalEndPoint);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _socket?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Builds the answer to a datagram.
    /// </summary>
    /// <param name="request">The datagram.</param>
    /// <param name="remote">The address it came from.</param>
    /// <returns>The answer, or <see langword="null"/> when the datagram is not a binding request.</returns>
    internal static byte[]? Answer(ReadOnlySpan<byte> request, IPEndPoint remote)
    {
        // A STUN message: type with the two top bits clear, length a multiple of four, the magic cookie.
        if (request.Length < StunConstants.HeaderSize || request.Length > MaxRequestSize
            || (request[0] & 0xC0) != 0
            || BinaryPrimitives.ReadUInt32BigEndian(request[4..]) != StunConstants.MagicCookie
            || StunMessage.MessageSize(request) != request.Length)
        {
            return null;
        }

        StunMessage message;
        try
        {
            message = StunMessage.Parse(request);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            // A datagram that only looks like STUN (odd length, a cut attribute) is ignored like any other garbage:
            // it must never stop the responder.
            return null;
        }

        if (message.Method != StunConstants.Binding || message.Class != StunConstants.ClassRequest)
        {
            return null;
        }

        return new StunMessage(StunConstants.Binding, StunConstants.ClassSuccess, message.TransactionId)
            .AddXorAddress(StunConstants.AttrXorMappedAddress, remote)
            .Encode();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("The responder is not started.");
        var buffer = new byte[2048];
        EndPoint any = socket.AddressFamily == AddressFamily.InterNetworkV6 ? new IPEndPoint(IPAddress.IPv6Any, 0) : new IPEndPoint(IPAddress.Any, 0);
        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                // Windows reports an ICMP "port unreachable" of an earlier answer as a failed receive.
                _logger.LogDebug("STUN receive: {Error}", ex.SocketErrorCode);
                continue;
            }

            if (received.RemoteEndPoint is not IPEndPoint remote)
            {
                continue;
            }

            var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
            if (Answer(buffer.AsSpan(0, received.ReceivedBytes), remote) is not { } answer)
            {
                continue;
            }

            if (!_limiter.TryAcquire(address))
            {
                _statistics.Refused();
                continue;
            }

            try
            {
                await socket.SendToAsync(answer, SocketFlags.None, remote, stoppingToken).ConfigureAwait(false);
                _statistics.StunAnswered();
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("STUN answer to {Remote}: {Error}", remote, ex.SocketErrorCode);
            }
        }
    }
}
