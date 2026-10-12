using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Localization;
using Golether.Security.Identity;

namespace Golether.Transports.Tls;

/// <summary>
/// Opens mutual-TLS streams over TCP and pins the remote key.
/// </summary>
public sealed class TlsPeerConnector : IPeerConnector
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
    /// The certificate context of the local identity.
    /// </summary>
    private readonly SslStreamCertificateContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="TlsPeerConnector"/> class.
    /// </summary>
    /// <param name="identity">The local identity.</param>
    /// <param name="options">The options.</param>
    public TlsPeerConnector(DeviceIdentity identity, TlsTransportOptions options)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _context = TlsProtocol.CreateContext(identity);
    }

    /// <inheritdoc />
    public async Task<PeerStream> ConnectAsync(PeerEndpoint endpoint, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
    {
        CheckExpected(expectedPeer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HandshakeTimeout);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            TlsProtocol.ConfigureSocket(socket, purpose, _options);
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw TimedOut(endpoint.ToString());
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw new IOException(Texts.Format("Net.Error.ConnectFailed", endpoint, ex.Message), ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return await AuthenticateAsync(socket, endpoint.ToString(), expectedPeer, purpose, timeout.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the handshake over a TCP connection opened by other means (for example punched through the routers with
    /// the help of a rendezvous server) and pins the remote key, exactly as <see cref="ConnectAsync(PeerEndpoint, PeerId, StreamPurpose, CancellationToken)"/> does.
    /// </summary>
    /// <param name="socket">The connected socket; ownership is transferred, it is closed on failure.</param>
    /// <param name="remoteAddress">The remote address for messages and diagnostics.</param>
    /// <param name="expectedPeer">The pinned peer identifier.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="PeerAuthenticationException">The peer presented another key.</exception>
    /// <exception cref="IOException">The handshake failed.</exception>
    public async Task<PeerStream> ConnectAsync(Socket socket, string remoteAddress, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        try
        {
            CheckExpected(expectedPeer);
            TlsProtocol.ConfigureSocket(socket, purpose, _options);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HandshakeTimeout);
        return await AuthenticateAsync(socket, remoteAddress, expectedPeer, purpose, timeout.Token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks that the expected peer is known and is not this device.
    /// </summary>
    /// <param name="expectedPeer">The pinned peer identifier.</param>
    private void CheckExpected(PeerId expectedPeer)
    {
        if (expectedPeer.IsEmpty)
        {
            throw new ArgumentException("The expected peer must be known.", nameof(expectedPeer));
        }

        if (expectedPeer == _identity.PeerId)
        {
            throw new ArgumentException("A device cannot connect to itself.", nameof(expectedPeer));
        }
    }

    /// <summary>
    /// Runs the TLS handshake as the client, pins the key and exchanges the preamble.
    /// </summary>
    /// <param name="socket">The connected socket; ownership is transferred.</param>
    /// <param name="remote">The remote address for messages.</param>
    /// <param name="expectedPeer">The pinned peer identifier.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="timeoutToken">Cancelled when the handshake takes too long or the caller cancels.</param>
    /// <param name="cancellationToken">The cancellation token of the caller.</param>
    /// <returns>The stream.</returns>
    private async Task<PeerStream> AuthenticateAsync(Socket socket, string remote, PeerId expectedPeer, StreamPurpose purpose, CancellationToken timeoutToken, CancellationToken cancellationToken)
    {
        SslStream? ssl = null;
        try
        {
            PeerId presented = default;
            ssl = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
            var authentication = new SslClientAuthenticationOptions
            {
                TargetHost = TlsProtocol.TargetHost,
                EnabledSslProtocols = TlsProtocol.Protocols,
                ClientCertificateContext = _context,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,

                // The chain and the name are irrelevant: only the pinned key matters.
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    TlsProtocol.TryGetPeer(certificate, out presented) && presented == expectedPeer,
            };

            try
            {
                await ssl.AuthenticateAsClientAsync(authentication, timeoutToken).ConfigureAwait(false);
            }
            catch (AuthenticationException ex)
            {
                var detail = presented.IsEmpty
                    ? Texts.Get("Net.Error.NoKeyPresented")
                    : Texts.Format("Net.Error.WrongKeyPresented", presented.ToShortString(), expectedPeer.ToShortString());
                throw new PeerAuthenticationException(Texts.Format("Net.Error.KeyCheckFailed", remote, detail), ex);
            }

            await ssl.WriteAsync(TlsProtocol.BuildPreamble(purpose), timeoutToken).ConfigureAwait(false);
            await ssl.FlushAsync(timeoutToken).ConfigureAwait(false);
            var answer = new byte[1];
            if (await ssl.ReadAtLeastAsync(answer, 1, throwOnEndOfStream: false, timeoutToken).ConfigureAwait(false) != 1
                || answer[0] != TlsProtocol.Accepted)
            {
                throw new IOException(Texts.Format("Net.Error.PeerRefused", remote));
            }

            var stream = new PeerStream(expectedPeer, purpose, ssl, remote)
            {
                LocalEndPoint = TlsProtocol.GetEndPoint(socket.LocalEndPoint),
                RemoteEndPoint = TlsProtocol.GetEndPoint(socket.RemoteEndPoint),
            };
            ssl = null;
            return stream;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(remote);
        }
        catch (SocketException ex)
        {
            throw new IOException(Texts.Format("Net.Error.ConnectFailed", remote, ex.Message), ex);
        }
        finally
        {
            if (ssl is not null)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
            }
            else if (!socket.Connected)
            {
                socket.Dispose();
            }
        }
    }

    /// <summary>
    /// Builds the error of a handshake that took too long.
    /// </summary>
    /// <param name="remote">The remote address.</param>
    /// <returns>The error.</returns>
    private IOException TimedOut(string remote)
        => new(Texts.Format("Net.Error.PeerTimedOut", remote, _options.HandshakeTimeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
}
