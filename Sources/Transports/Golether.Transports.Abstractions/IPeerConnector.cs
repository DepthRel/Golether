using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;

namespace Golether.Transports;

/// <summary>
/// Opens authenticated streams to peers.
/// </summary>
public interface IPeerConnector
{
    /// <summary>
    /// Connects to a peer and verifies that it owns the expected key.
    /// </summary>
    /// <param name="endpoint">The peer address.</param>
    /// <param name="expectedPeer">The pinned peer identifier.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="PeerAuthenticationException">The peer presented another key.</exception>
    /// <exception cref="IOException">The connection failed.</exception>
    Task<PeerStream> ConnectAsync(PeerEndpoint endpoint, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken);
}
