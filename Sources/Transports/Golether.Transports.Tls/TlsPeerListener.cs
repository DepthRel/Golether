using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;
using Golether.Core.Identity;
using Golether.Security.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.Tls;

/// <summary>
/// Accepts mutual-TLS streams over TCP on all interfaces (IPv4 and IPv6).
/// </summary>
/// <remarks>
/// Every device certificate is accepted at the TLS level because newcomers are unknown: the listener derives the
/// <see cref="PeerId"/> from the key the peer proved to own, and the session layer decides whether the peer may join.
/// </remarks>
public sealed class TlsPeerListener : IPeerListener
{
    /// <summary>
    /// The local identity.
    /// </summary>
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly TlsTransportOptions _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<TlsPeerListener> _logger;

    /// <summary>
    /// The certificate context of the local identity.
    /// </summary>
    private readonly SslStreamCertificateContext _context;

    /// <summary>
    /// Authenticated streams waiting for <see cref="AcceptAsync"/>.
    /// </summary>
    private readonly Channel<PeerStream> _accepted = Channel.CreateBounded<PeerStream>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.Wait,
    });

    /// <summary>
    /// Limits concurrent handshakes.
    /// </summary>
    private readonly SemaphoreSlim _handshakeSlots;

    /// <summary>
    /// Stops the accept loop.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// The listening socket.
    /// </summary>
    private readonly TcpListener _listener;

    /// <summary>
    /// The accept loop.
    /// </summary>
    private Task? _acceptLoop;

    /// <summary>
    /// Initializes a new instance of the <see cref="TlsPeerListener"/> class.
    /// </summary>
    /// <param name="identity">The local identity.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public TlsPeerListener(DeviceIdentity identity, TlsTransportOptions options, ILogger<TlsPeerListener>? logger = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger ?? NullLogger<TlsPeerListener>.Instance;
        _context = TlsProtocol.CreateContext(identity);
        _handshakeSlots = new SemaphoreSlim(_options.MaxPendingHandshakes, _options.MaxPendingHandshakes);
        _listener = new TcpListener(IPAddress.IPv6Any, _options.Port);
        _listener.Server.DualMode = true;
    }

    /// <inheritdoc />
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Starts listening.
    /// </summary>
    /// <exception cref="SocketException">The port is not available.</exception>
    public void Start()
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        _listener.Start();
        _logger.LogInformation("Listening on port {Port}", Port);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stopping.Token));
    }

    /// <inheritdoc />
    public ValueTask<PeerStream> AcceptAsync(CancellationToken cancellationToken)
    {
        if (_acceptLoop is null)
        {
            throw new InvalidOperationException("The listener is not started.");
        }

        return _accepted.Reader.ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            await _acceptLoop.ConfigureAwait(false);
        }

        _accepted.Writer.TryComplete();
        while (_accepted.Reader.TryRead(out var pending))
        {
            await pending.DisposeAsync().ConfigureAwait(false);
        }

        _stopping.Dispose();
    }

    /// <summary>
    /// Accepts TCP connections and hands them to handshake tasks.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the listener stops.</returns>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                await _handshakeSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                socket = await _listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                       || (ex is SocketException && cancellationToken.IsCancellationRequested))
            {
                return;
            }
            catch (SocketException ex)
            {
                _handshakeSlots.Release();
                _logger.LogWarning(ex, "Accepting a connection failed");
                continue;
            }

            _ = Task.Run(() => HandshakeAsync(socket, cancellationToken), CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs the TLS handshake and the preamble of an accepted connection.
    /// </summary>
    /// <param name="socket">The accepted socket.</param>
    /// <param name="cancellationToken">Stops the listener.</param>
    /// <returns>A task that completes when the stream is queued or dropped.</returns>
    private async Task HandshakeAsync(Socket socket, CancellationToken cancellationToken)
    {
        var remote = socket.RemoteEndPoint?.ToString();
        SslStream? ssl = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.HandshakeTimeout);

            ssl = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
            PeerId peer = default;
            var authentication = new SslServerAuthenticationOptions
            {
                ServerCertificateContext = _context,
                ClientCertificateRequired = true,
                EnabledSslProtocols = TlsProtocol.Protocols,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    TlsProtocol.TryGetPeer(certificate, out peer) && peer != _identity.PeerId,
            };
            await ssl.AuthenticateAsServerAsync(authentication, timeout.Token).ConfigureAwait(false);

            var preamble = new byte[6];
            if (await ssl.ReadAtLeastAsync(preamble, preamble.Length, throwOnEndOfStream: false, timeout.Token).ConfigureAwait(false) != preamble.Length
                || !TlsProtocol.TryParsePreamble(preamble, out var purpose))
            {
                _logger.LogWarning("Dropped {Remote}: invalid preamble", remote);
                return;
            }

            TlsProtocol.ConfigureSocket(socket, purpose, _options);
            await ssl.WriteAsync(new[] { TlsProtocol.Accepted }, timeout.Token).ConfigureAwait(false);
            await ssl.FlushAsync(timeout.Token).ConfigureAwait(false);

            var stream = new PeerStream(peer, purpose, ssl, remote);
            await _accepted.Writer.WriteAsync(stream, cancellationToken).ConfigureAwait(false);
            ssl = null;
            _logger.LogDebug("Accepted {Purpose} stream from {Peer} ({Remote})", purpose, peer.ToShortString(), remote);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException
                                   or SocketException or ChannelClosedException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Dropped connection from {Remote}: {Reason}", remote, ex.Message);
            }
        }
        finally
        {
            if (ssl is not null)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
            }

            _handshakeSlots.Release();
        }
    }
}
