using System.Net;
using System.Net.Sockets;
using Golether.Core.Networking;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Transports.Rendezvous.Punching;

namespace Golether.Transports.Rendezvous;

/// <summary>
/// One connection of a device to the rendezvous server, opened from a port that punching may share.
/// </summary>
internal sealed class RendezvousConnection : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousConnection"/> class.
    /// </summary>
    /// <param name="channel">The message channel; owns the socket.</param>
    /// <param name="localPort">The local port of the connection.</param>
    /// <param name="nonce">The nonce of the server.</param>
    private RendezvousConnection(RendezvousChannel channel, int localPort, string nonce)
    {
        Channel = channel;
        LocalPort = localPort;
        Nonce = nonce;
    }

    /// <summary>
    /// Gets the message channel.
    /// </summary>
    public RendezvousChannel Channel { get; }

    /// <summary>
    /// Gets the local port; the server saw this device on it, so punching starts from it.
    /// </summary>
    public int LocalPort { get; }

    /// <summary>
    /// Gets the nonce the server greeted with.
    /// </summary>
    public string Nonce { get; }

    /// <summary>
    /// Connects to the server over IPv4 and reads its greeting.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="timeout">The time allowed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="RendezvousException">The server is unreachable or speaks another version.</exception>
    public static async Task<RendezvousConnection> OpenAsync(PeerEndpoint server, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        Socket? socket = null;
        RendezvousChannel? channel = null;
        try
        {
            var address = await ResolveAsync(server.Host, limit.Token).ConfigureAwait(false);
            socket = TcpPunch.Bind(0);
            await socket.ConnectAsync(new IPEndPoint(address, server.Port), limit.Token).ConfigureAwait(false);
            var localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
            channel = new RendezvousChannel(new NetworkStream(socket, ownsSocket: true));
            socket = null;
            var hello = await channel.ReceiveAsync(limit.Token).ConfigureAwait(false);
            if (hello is not ServerHelloMessage { Nonce: var nonce } greeting || !RendezvousProtocol.IsValidRandomId(nonce))
            {
                throw new RendezvousException(hello is ErrorMessage error ? error.Error : RendezvousError.BadRequest);
            }

            if (greeting.Version != RendezvousProtocol.Version)
            {
                throw new RendezvousException(RendezvousError.UnsupportedVersion);
            }

            var connection = new RendezvousConnection(channel, localPort, nonce);
            channel = null;
            return connection;
        }
        catch (Exception ex) when (ex is SocketException or IOException and not RendezvousException or InvalidDataException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new RendezvousException(RendezvousError.Unreachable, ex);
        }
        finally
        {
            socket?.Dispose();
            if (channel is not null)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Receives the next message within a time limit.
    /// </summary>
    /// <param name="timeout">The time allowed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message, or <see langword="null"/> when the server closed the connection.</returns>
    /// <exception cref="RendezvousException">The server went silent or sent garbage.</exception>
    public async Task<RendezvousMessage?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            return await Channel.ReceiveAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RendezvousException(RendezvousError.Timeout, ex);
        }
        catch (Exception ex) when (ex is IOException and not RendezvousException or InvalidDataException or SocketException)
        {
            throw new RendezvousException(RendezvousError.Unreachable, ex);
        }
    }

    /// <summary>
    /// Waits for the addresses of the other side.
    /// </summary>
    /// <param name="timeout">The time allowed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The addresses to punch towards: the public one first.</returns>
    /// <exception cref="RendezvousException">The server refused or the answer was unusable.</exception>
    public async Task<IReadOnlyList<IPEndPoint>> ReceivePunchAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        switch (await ReceiveAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            case PunchMessage punch when RendezvousProtocol.TryParseEndpoint(punch.Peer, out var peer):
                return [peer, .. RendezvousProtocol.ParseLocals(punch.PeerLocals).Where(l => !l.Equals(peer))];
            case ErrorMessage error:
                throw new RendezvousException(error.Error);
            case null:
                throw new RendezvousException(RendezvousError.Unreachable);
            default:
                throw new RendezvousException(RendezvousError.BadRequest);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Channel.DisposeAsync();

    /// <summary>
    /// Finds the IPv4 address of the server.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address.</returns>
    /// <exception cref="RendezvousException">The server has no IPv4 address.</exception>
    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal.AddressFamily == AddressFamily.InterNetwork ? literal : throw new RendezvousException(RendezvousError.Unreachable);
        }

        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
        return addresses.Length > 0 ? addresses[0] : throw new RendezvousException(RendezvousError.Unreachable);
    }
}
