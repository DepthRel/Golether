using Golether.Core.Identity;
using Golether.Core.Networking;

namespace Golether.Transports;

/// <summary>
/// The purpose of a peer stream, sent in the stream preamble.
/// </summary>
public enum StreamPurpose : byte
{
    /// <summary>
    /// Session control: hello, playback state, clock synchronization, statuses.
    /// </summary>
    Control = 1,

    /// <summary>
    /// Media data: chunk requests and responses. Several data streams run in parallel.
    /// </summary>
    MediaData = 2,
}

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

/// <summary>
/// Accepts authenticated streams from peers.
/// </summary>
public interface IPeerListener : IAsyncDisposable
{
    /// <summary>
    /// Gets the local port the listener is bound to.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Waits for the next authenticated stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream; the caller owns it.</returns>
    ValueTask<PeerStream> AcceptAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The peer did not prove ownership of the expected key.
/// </summary>
public sealed class PeerAuthenticationException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeerAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PeerAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
