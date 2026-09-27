using System.Net.Sockets;
using Golether.Core.Networking;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Transports.Rendezvous.Punching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.Rendezvous;

/// <summary>
/// The participant's side of a meeting: asks the server for the host of a room and punches a TCP connection to it.
/// </summary>
public sealed class RendezvousDialer
{
    /// <summary>
    /// The options.
    /// </summary>
    private readonly RendezvousClientOptions _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousDialer"/> class.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="logger">The logger.</param>
    public RendezvousDialer(RendezvousClientOptions? options = null, ILogger<RendezvousDialer>? logger = null)
    {
        _options = options ?? new RendezvousClientOptions();
        _options.Validate();
        _logger = logger ?? NullLogger<RendezvousDialer>.Instance;
    }

    /// <summary>
    /// Opens a TCP connection to the host of a room. The connection is not authenticated yet: the caller runs its
    /// handshake over it and checks the key of the host.
    /// </summary>
    /// <param name="point">The server and the room.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connected socket; the caller owns it.</returns>
    /// <exception cref="RendezvousException">The meeting did not happen.</exception>
    public async Task<Socket> DialAsync(RendezvousPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);

        // The connection to the server stays open while punching: it keeps the router mapping of the port alive.
        await using var connection = await RendezvousConnection.OpenAsync(point.Server, _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
        await SendAsync(connection, new ConnectMessage(point.Room, LocalAddresses.Describe(connection.LocalPort)), cancellationToken).ConfigureAwait(false);
        var targets = await connection.ReceivePunchAsync(_options.AnswerTimeout, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Punching from port {Port} towards {Targets}", connection.LocalPort, string.Join(", ", targets));
        var socket = await TcpPunch.ConnectAsync(connection.LocalPort, targets, _options.PunchTimeout, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Met the host of the room through {Server} at {Remote}", point.Server, socket.RemoteEndPoint);
        return socket;
    }

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    /// <exception cref="RendezvousException">The connection broke.</exception>
    internal static async Task SendAsync(RendezvousConnection connection, RendezvousMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await connection.Channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            throw new RendezvousException(RendezvousError.Unreachable, ex);
        }
    }
}
