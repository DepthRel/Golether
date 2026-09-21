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
        if (expectedPeer.IsEmpty)
        {
            throw new ArgumentException("The expected peer must be known.", nameof(expectedPeer));
        }

        if (expectedPeer == _identity.PeerId)
        {
            throw new ArgumentException("A device cannot connect to itself.", nameof(expectedPeer));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HandshakeTimeout);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        SslStream? ssl = null;
        try
        {
            TlsProtocol.ConfigureSocket(socket, purpose, _options);
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token).ConfigureAwait(false);

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
                await ssl.AuthenticateAsClientAsync(authentication, timeout.Token).ConfigureAwait(false);
            }
            catch (AuthenticationException ex)
            {
                var detail = presented.IsEmpty
                    ? Texts.Get("Net.Error.NoKeyPresented")
                    : Texts.Format("Net.Error.WrongKeyPresented", presented.ToShortString(), expectedPeer.ToShortString());
                throw new PeerAuthenticationException(Texts.Format("Net.Error.KeyCheckFailed", endpoint, detail), ex);
            }

            await ssl.WriteAsync(TlsProtocol.BuildPreamble(purpose), timeout.Token).ConfigureAwait(false);
            await ssl.FlushAsync(timeout.Token).ConfigureAwait(false);
            var answer = new byte[1];
            if (await ssl.ReadAtLeastAsync(answer, 1, throwOnEndOfStream: false, timeout.Token).ConfigureAwait(false) != 1
                || answer[0] != TlsProtocol.Accepted)
            {
                throw new IOException(Texts.Format("Net.Error.PeerRefused", endpoint));
            }

            var stream = new PeerStream(expectedPeer, purpose, ssl, endpoint.ToString());
            ssl = null;
            return stream;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException(Texts.Format("Net.Error.PeerTimedOut", endpoint, _options.HandshakeTimeout.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (SocketException ex)
        {
            throw new IOException(Texts.Format("Net.Error.ConnectFailed", endpoint, ex.Message), ex);
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
}
