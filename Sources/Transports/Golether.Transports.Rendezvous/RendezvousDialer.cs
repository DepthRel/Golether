using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Enums;
using Golether.Core.Networking;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Transports.Rendezvous.Punching;

namespace Golether.Transports.Rendezvous;

/// <summary>
/// The participant's side of a meeting: asks the server for the host of a room and punches a TCP connection to it.
/// One dialer serves all streams of a session, so it can keep them from meeting all at once
/// (<see cref="RendezvousClientOptions.MaxConcurrentDials"/>).
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
    /// Lets through as many meetings at once as the options allow; the others wait for their turn.
    /// </summary>
    private readonly SemaphoreSlim _turns;

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
        _turns = new SemaphoreSlim(_options.MaxConcurrentDials, _options.MaxConcurrentDials);
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

        // The wait for a turn is not part of any timeout below: those start when the meeting does.
        await _turns.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The connection to the server stays open while punching: it keeps the router mapping of the port alive.
            await using var connection = await RendezvousConnection.OpenAsync(point.Server, _options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            await SendAsync(connection, new ConnectMessage(point.Room, LocalAddresses.Describe(connection.LocalPort)), cancellationToken).ConfigureAwait(false);
            var targets = await connection.ReceivePunchAsync(_options.AnswerTimeout, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Punching from port {Port} towards {Targets}", connection.LocalPort, string.Join(", ", targets));
            var socket = await TcpPunch.ConnectAsync(connection.LocalPort, targets, _options.PunchTimeout, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Met the host of the room through {Server} at {Remote}", point.Server, socket.RemoteEndPoint);
            return socket;
        }
        finally
        {
            _turns.Release();
        }
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
