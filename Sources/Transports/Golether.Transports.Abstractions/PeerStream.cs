using Golether.Core.Data.Enums;
using Golether.Core.Identity;

namespace Golether.Transports;

/// <summary>
/// An authenticated, encrypted byte stream to a peer.
/// </summary>
/// <remarks>
/// <see cref="RemotePeer"/> is derived by the transport from the key the peer proved to own during the handshake.
/// </remarks>
public sealed class PeerStream : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeerStream"/> class.
    /// </summary>
    /// <param name="remotePeer">The authenticated remote identifier.</param>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="stream">The encrypted stream; ownership is transferred.</param>
    /// <param name="remoteAddress">The remote network address for diagnostics.</param>
    public PeerStream(PeerId remotePeer, StreamPurpose purpose, Stream stream, string? remoteAddress)
    {
        if (remotePeer.IsEmpty)
        {
            throw new ArgumentException("The remote peer must be known.", nameof(remotePeer));
        }

        RemotePeer = remotePeer;
        Purpose = purpose;
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        RemoteAddress = remoteAddress;
    }

    /// <summary>
    /// Gets the authenticated remote identifier.
    /// </summary>
    public PeerId RemotePeer { get; }

    /// <summary>
    /// Gets the stream purpose.
    /// </summary>
    public StreamPurpose Purpose { get; }

    /// <summary>
    /// Gets the encrypted stream.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Gets the remote network address for diagnostics.
    /// </summary>
    public string? RemoteAddress { get; }

    /// <summary>
    /// Closes the stream.
    /// </summary>
    /// <returns>A task that completes when the stream is closed.</returns>
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
