using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;

namespace Golether.Transports;

/// <summary>
/// Opens authenticated streams to a host that cannot be reached directly, meeting it through a rendezvous server.
/// The server only introduces the two sides; the stream goes directly between them.
/// </summary>
public interface IRendezvousConnector
{
    /// <summary>
    /// Meets the host of a room and verifies that it owns the expected key.
    /// </summary>
    /// <param name="point">The rendezvous server and the room of the host.</param>
    /// <param name="expectedPeer">The pinned identifier of the host.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="PeerAuthenticationException">The peer presented another key.</exception>
    /// <exception cref="IOException">The meeting or the connection failed.</exception>
    Task<PeerStream> ConnectAsync(RendezvousPoint point, PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken);
}
