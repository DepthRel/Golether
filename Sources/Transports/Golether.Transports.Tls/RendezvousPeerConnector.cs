using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Transports.Rendezvous;

namespace Golether.Transports.Tls;

/// <summary>
/// Meets the host through a rendezvous server and runs the same mutual-TLS handshake with the pinned key over the
/// punched connection. The server never sees the session: it only told both sides where to knock.
/// </summary>
public sealed class RendezvousPeerConnector : IRendezvousConnector
{
    /// <summary>
    /// Runs the TLS handshake.
    /// </summary>
    private readonly TlsPeerConnector _tls;

    /// <summary>
    /// Punches the TCP connection.
    /// </summary>
    private readonly RendezvousDialer _dialer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousPeerConnector"/> class.
    /// </summary>
    /// <param name="tls">Runs the TLS handshake.</param>
    /// <param name="dialer">Punches the TCP connection.</param>
    public RendezvousPeerConnector(TlsPeerConnector tls, RendezvousDialer dialer)
    {
        _tls = tls ?? throw new ArgumentNullException(nameof(tls));
        _dialer = dialer ?? throw new ArgumentNullException(nameof(dialer));
    }

    /// <inheritdoc />
    public async Task<PeerStream> ConnectAsync(RendezvousPoint point, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        var socket = await _dialer.DialAsync(point, cancellationToken).ConfigureAwait(false);
        var remote = socket.RemoteEndPoint?.ToString() ?? point.Server.ToString();
        return await _tls.ConnectAsync(socket, remote, expectedPeer, purpose, cancellationToken).ConfigureAwait(false);
    }
}
