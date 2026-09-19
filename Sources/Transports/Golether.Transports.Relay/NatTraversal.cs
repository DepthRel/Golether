using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

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

/// <summary>
/// Finds the public address of a UDP port and opens a path to a peer through the routers on both sides.
/// </summary>
/// <remarks>
/// <para>
/// A tunnel between two home computers fails because neither router lets an unexpected packet in. Both sides learn
/// their own public address from a STUN server, put it into the offer and the answer they exchange by hand, and then
/// send packets towards each other. The first packet of each side is dropped by the other router but leaves a hole
/// in its own, so the next one gets through. From then on the tunnel handshake passes.
/// </para>
/// <para>
/// The port used here is the port the tunnel will listen on, and the socket is released right before the tunnel
/// starts: the mapping of the router lives long enough (tens of seconds at least) for the handshake to follow.
/// </para>
/// </remarks>
public static class NatTraversal
{
    /// <summary>
    /// The public STUN servers asked by default. They answer with the address and nothing else; no account, no data.
    /// </summary>
    public static IReadOnlyList<string> DefaultServers { get; } =
    [
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302",
        "stun.cloudflare.com:3478",
    ];

    /// <summary>
    /// The time one server gets to answer, over all attempts.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long to wait before asking the same server again. UDP has no delivery to speak of, so the request is
    /// repeated a few times inside <see cref="RequestTimeout"/>.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Asks the STUN servers how this UDP port looks from outside.
    /// </summary>
    /// <param name="socket">A bound UDP socket; its port is the one the answer describes.</param>
    /// <param name="servers">The servers to ask, or <see langword="null"/> for <see cref="DefaultServers"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public address, or <see langword="null"/> when no server answered.</returns>
    public static async Task<PublicEndpoint?> DiscoverAsync(Socket socket, IReadOnlyList<string>? servers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        var localPort = (socket.LocalEndPoint as IPEndPoint)?.Port ?? 0;
        var answers = new List<IPEndPoint>();
        foreach (var server in servers ?? DefaultServers)
        {
            if (await AskAsync(socket, server, cancellationToken).ConfigureAwait(false) is { } answer)
            {
                answers.Add(answer);
                if (answers.Count == 2)
                {
                    break;
                }
            }
        }

        if (answers.Count == 0)
        {
            return null;
        }

        // Two servers seeing the same port means the router keeps one mapping per local port, and the address is
        // worth putting into an invitation.
        var predictable = answers.Count > 1 && answers[0].Port == answers[1].Port && answers[0].Address.Equals(answers[1].Address);
        return new PublicEndpoint(answers[0], localPort, predictable);
    }

    /// <summary>
    /// Sends a few packets towards the addresses of the other side, so its answers are let in afterwards.
    /// </summary>
    /// <param name="socket">The bound UDP socket the tunnel will use.</param>
    /// <param name="targets">The addresses of the other side.</param>
    /// <param name="rounds">How many times each address is poked.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the packets are away.</returns>
    public static async Task PunchAsync(Socket socket, IReadOnlyList<IPEndPoint> targets, int rounds = 5, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(targets);

        // The payload is never read by anybody: WireGuard drops it as a malformed packet, which is exactly the point.
        var payload = new byte[] { 0, 0, 0, 0 };
        for (var round = 0; round < rounds && !cancellationToken.IsCancellationRequested; round++)
        {
            foreach (var target in targets)
            {
                try
                {
                    await socket.SendToAsync(payload, SocketFlags.None, target, cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    Trace.WriteLine($"Golether punch to {target}: {ex.SocketErrorCode}");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens a UDP socket on a port, ready for <see cref="DiscoverAsync"/> and <see cref="PunchAsync"/>.
    /// </summary>
    /// <param name="port">The port, or <c>0</c> for any free one.</param>
    /// <returns>The socket; the caller closes it before the tunnel takes the port.</returns>
    public static Socket Open(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asks one server and waits for its answer.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="server">The server as <c>host:port</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public address, or <see langword="null"/>.</returns>
    private static async Task<IPEndPoint?> AskAsync(Socket socket, string server, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(server, cancellationToken).ConfigureAwait(false) is not { } address)
        {
            return null;
        }

        var request = new StunMessage(Stun.Binding, Stun.ClassRequest, RandomNumberGenerator.GetBytes(12));
        var encoded = request.Encode();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var buffer = new byte[StunMessage.MaxSize];
        try
        {
            await socket.SendToAsync(encoded, SocketFlags.None, address, timeout.Token).ConfigureAwait(false);
            var nextAttempt = DateTime.UtcNow + RetryDelay;
            while (!timeout.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= nextAttempt)
                {
                    // The question or the answer was lost, or the machine was busy: ask again.
                    await socket.SendToAsync(encoded, SocketFlags.None, address, timeout.Token).ConfigureAwait(false);
                    nextAttempt = DateTime.UtcNow + RetryDelay;
                }

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                attempt.CancelAfter(RetryDelay);
                SocketReceiveFromResult received;
                try
                {
                    received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), attempt.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                {
                    continue;
                }

                if (received.ReceivedBytes < Stun.HeaderSize)
                {
                    continue;
                }

                var message = StunMessage.Parse(buffer.AsSpan(0, received.ReceivedBytes));
                if (message.Method != Stun.Binding || message.Class != Stun.ClassSuccess
                    || !message.TransactionId.AsSpan().SequenceEqual(request.TransactionId))
                {
                    continue;
                }

                return message.GetXorAddress(Stun.AttrXorMappedAddress);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The server did not answer in time; the next one is tried.
        }
        catch (Exception ex) when (ex is SocketException or FormatException or ArgumentException)
        {
            Trace.WriteLine($"Golether STUN {server}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Turns <c>host:port</c> into an address.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private static async Task<IPEndPoint?> ResolveAsync(string server, CancellationToken cancellationToken)
    {
        var separator = server.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(server[(separator + 1)..], out var port))
        {
            return null;
        }

        var host = server[..separator];
        if (IPAddress.TryParse(host, out var literal))
        {
            return new IPEndPoint(literal, port);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
            return addresses.Length == 0 ? null : new IPEndPoint(addresses[0], port);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return null;
        }
    }
}
