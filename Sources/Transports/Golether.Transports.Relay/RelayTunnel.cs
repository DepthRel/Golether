using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.Relay;

/// <summary>
/// The local entrance to the relay of the host. The cameras and voices of a participant can only be pointed at an
/// address, so the tunnel listens on a loopback port and carries every connection made to it over a stream to the
/// host that the session opens by its own route.
/// </summary>
/// <remarks>
/// Only programs of this device can connect, and the tunnel adds no rights: what arrives at the host still needs
/// the credentials of the relay.
/// </remarks>
public sealed class RelayTunnel : IAsyncDisposable
{
    /// <summary>
    /// The largest number of connections carried at once: the relay of the host does not grant more allocations.
    /// </summary>
    private const int MaxConnections = 16;

    /// <summary>
    /// Opens a stream to the relay of the host.
    /// </summary>
    private readonly Func<CancellationToken, Task<Stream>> _open;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The loopback listener.
    /// </summary>
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    /// <summary>
    /// Limits the connections carried at once.
    /// </summary>
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);

    /// <summary>
    /// Stops the tunnel.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// The accept loop.
    /// </summary>
    private Task _acceptLoop = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayTunnel"/> class.
    /// </summary>
    /// <param name="open">Opens a stream to the relay of the host; the tunnel owns the stream.</param>
    /// <param name="logger">The logger.</param>
    public RelayTunnel(Func<CancellationToken, Task<Stream>> open, ILogger<RelayTunnel>? logger = null)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _logger = logger ?? NullLogger<RelayTunnel>.Instance;
    }

    /// <summary>
    /// Gets the loopback port of the tunnel; known after <see cref="Start"/>.
    /// </summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Starts accepting local connections.
    /// </summary>
    /// <exception cref="SocketException">No loopback port could be opened.</exception>
    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        _logger.LogInformation("The relay of the host is reached through local port {Port}", Port);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _acceptLoop.ConfigureAwait(false);
        _stop.Dispose();
    }

    /// <summary>
    /// Accepts local connections until disposal.
    /// </summary>
    /// <returns>A task that completes when the tunnel stops.</returns>
    private async Task AcceptLoopAsync()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                       || (ex is SocketException && token.IsCancellationRequested))
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("Accepting a local relay connection failed: {Error}", ex.SocketErrorCode);
                continue;
            }

            if (!_slots.Wait(0, CancellationToken.None))
            {
                _logger.LogWarning("Refused a local relay connection: {Count} are carried already", MaxConnections);
                client.Dispose();
                continue;
            }

            _ = Task.Run(() => CarryAsync(client, token), CancellationToken.None);
        }
    }

    /// <summary>
    /// Carries one local connection over its own stream to the host until either end closes.
    /// </summary>
    /// <param name="client">The local connection.</param>
    /// <param name="cancellationToken">Stops the tunnel.</param>
    /// <returns>A task that completes when both ends are closed.</returns>
    private async Task CarryAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var near = client.GetStream();
                var far = await _open(cancellationToken).ConfigureAwait(false);
                await using (far.ConfigureAwait(false))
                {
                    _logger.LogInformation("Opened a stream to the relay of the host");
                    using var ending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var outgoing = PumpAsync(near, far, ending.Token);
                    var incoming = PumpAsync(far, near, ending.Token);

                    // One direction ending means the connection is over: the other one must not wait for bytes
                    // that will never come.
                    await Task.WhenAny(outgoing, incoming).ConfigureAwait(false);
                    await ending.CancelAsync().ConfigureAwait(false);
                    await Task.WhenAll(outgoing, incoming).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException
                                       or InvalidOperationException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("The relay of the host was not reached: {Reason}", ex.Message);
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// Copies bytes one way, sending each read at once: these are voice and video packets.
    /// </summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The destination.</param>
    /// <param name="cancellationToken">Stops copying.</param>
    /// <returns>A task that completes when the source ended, either end failed or copying was stopped.</returns>
    private static async Task PumpAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                await to.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection is over; the caller closes both ends.
        }
    }
}
