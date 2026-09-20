using Golether.Core.Identity;

namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// The owner of peers: sends signaling and receives decoded media.
/// </summary>
internal interface IPeerHost
{
    /// <summary>
    /// Sends signaling data to a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="payload">The payload.</param>
    void SendSignal(PeerId peer, string kind, string payload);

    /// <summary>
    /// Delivers a decoded BGRA frame.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="pixels">The pixels, valid only during the call.</param>
    void DeliverVideo(PeerId peer, int width, int height, ReadOnlySpan<byte> pixels);

    /// <summary>
    /// Delivers decoded PCM (S16LE, 48 kHz, mono).
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="pcm">The samples, valid only during the call.</param>
    void DeliverAudio(WebRtcPeer peer, ReadOnlySpan<byte> pcm);

    /// <summary>
    /// Drops a peer whose DTLS connection failed or could not be verified.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="reason">The reason for the log.</param>
    void RejectPeer(WebRtcPeer peer, string reason);

    /// <summary>
    /// Delivers a data channel message of a verified peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="message">The message, valid only during the call.</param>
    void DeliverData(PeerId peer, ReadOnlySpan<byte> message);
}
