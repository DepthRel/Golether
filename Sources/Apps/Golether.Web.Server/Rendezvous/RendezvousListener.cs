using System.Net;
using System.Net.Sockets;
using Golether.Localization;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// Accepts the TCP connections of devices on the configured address and port and hands them to
/// <see cref="ConnectionHandler"/>, within the limits of connections at once and per address.
/// </summary>
internal sealed class RendezvousListener : BackgroundService
{
    /// <summary>
    /// The options.
    /// </summary>
    private readonly RendezvousServerOptions _options;

    /// <summary>
    /// Serves the connections.
    /// </summary>
    private readonly ConnectionHandler _handler;

    /// <summary>
    /// The counters.
    /// </summary>
    private readonly ServerStatistics _statistics;

    /// <summary>
    /// Limits new connections per address.
    /// </summary>
    private readonly AddressRateLimiter _limiter;

    /// <summary>
    /// Limits connections at once.
    /// </summary>
    private readonly SemaphoreSlim _slots;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<RendezvousListener> _logger;

    /// <summary>
    /// The listening socket, or <see langword="null"/> before the start.
    /// </summary>
    private TcpListener? _listener;

    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousListener"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="handler">Serves the connections.</param>
    /// <param name="statistics">The counters.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public RendezvousListener(RendezvousServerOptions options, ConnectionHandler handler, ServerStatistics statistics, TimeProvider timeProvider, ILogger<RendezvousListener> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _limiter = new AddressRateLimiter(options.MaxConnectionsPerMinute, timeProvider);
        _slots = new SemaphoreSlim(options.MaxConnections, options.MaxConnections);
    }

    /// <summary>
    /// Gets the address the server listens on; the port is known after the start even when 0 was configured.
    /// </summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    /// <summary>
    /// Opens the port before the host reports the start, so a busy port stops the server with a clear message.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the loop runs.</returns>
    /// <exception cref="InvalidOperationException">The port is busy or the address is not on this machine.</exception>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(_options.ParsedListenAddress, _options.Port);
        var listener = new TcpListener(endpoint);
        if (endpoint.Address.Equals(IPAddress.IPv6Any))
        {
            listener.Server.DualMode = true;
        }

        try
        {
            listener.Start(backlog: 512);
        }
        catch (SocketException ex)
        {
            listener.Dispose();
            throw new InvalidOperationException(Texts.Format("Server.Error.PortBusy", "tcp://" + endpoint, ex.Message), ex);
        }

        _listener = listener;
        _logger.LogInformation("Rendezvous server listening on tcp://{Endpoint}", listener.LocalEndpoint);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Stop();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _listener?.Dispose();
        _slots.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = _listener ?? throw new InvalidOperationException("The listener is not started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptSocketAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                       || (ex is SocketException && stoppingToken.IsCancellationRequested))
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning("Accepting a connection failed: {Error}", ex.SocketErrorCode);
                continue;
            }

            var remote = (socket.RemoteEndPoint as IPEndPoint)?.Address;
            if (remote is null || !_limiter.TryAcquire(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote) || !_slots.Wait(0))
            {
                _statistics.Refused();
                socket.Dispose();
                continue;
            }

            _ = Task.Run(() => ServeAsync(socket, stoppingToken), CancellationToken.None);
        }
    }

    /// <summary>
    /// Serves one connection and frees its slot.
    /// </summary>
    /// <param name="socket">The socket.</param>
    /// <param name="stoppingToken">Stops the server.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    private async Task ServeAsync(Socket socket, CancellationToken stoppingToken)
    {
        _statistics.ConnectionOpened();
        try
        {
            socket.NoDelay = true;
            await _handler.HandleAsync(socket, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "A connection failed unexpectedly");
            socket.Dispose();
        }
        finally
        {
            _statistics.ConnectionClosed();
            _slots.Release();
        }
    }
}
