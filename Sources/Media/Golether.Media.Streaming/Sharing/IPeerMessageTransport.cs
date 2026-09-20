using Golether.Core.Identity;

namespace Golether.Media.Streaming.Sharing;

/// <summary>
/// Reliable, ordered, authenticated message channels to other participants (WebRTC data channels).
/// </summary>
public interface IPeerMessageTransport
{
    /// <summary>
    /// Raised when a message arrives. May be raised on any thread.
    /// </summary>
    event EventHandler<PeerMessage>? MessageReceived;

    /// <summary>
    /// Gets the participants with an open channel.
    /// </summary>
    IReadOnlyCollection<PeerId> ConnectedPeers { get; }

    /// <summary>
    /// Sends a message.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="message">The message.</param>
    /// <returns><see langword="false"/> when the channel is not open or its buffer is full; try again later.</returns>
    bool TrySend(PeerId peer, ReadOnlySpan<byte> message);
}
