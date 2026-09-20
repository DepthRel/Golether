using Golether.Core.Identity;
using Golether.Media.Conference;
using Golether.Media.Streaming.Sharing;

namespace Golether.Session;

/// <summary>
/// <see cref="IPeerMessageTransport"/> over the WebRTC data channels of the conference.
/// </summary>
public sealed class ConferenceMessageTransport : IPeerMessageTransport, IDisposable
{
    /// <summary>
    /// The conferencing backend.
    /// </summary>
    private readonly IConferenceMedia _media;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConferenceMessageTransport"/> class.
    /// </summary>
    /// <param name="media">The conferencing backend.</param>
    public ConferenceMessageTransport(IConferenceMedia media)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _media.DataReceived += OnData;
    }

    /// <inheritdoc />
    public event EventHandler<PeerMessage>? MessageReceived;

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> ConnectedPeers => _media.DataPeers;

    /// <inheritdoc />
    public bool TrySend(PeerId peer, ReadOnlySpan<byte> message) => _media.TrySendData(peer, message);

    /// <inheritdoc />
    public void Dispose() => _media.DataReceived -= OnData;

    /// <summary>
    /// Passes a copy of a message on.
    /// </summary>
    /// <param name="sender">The backend.</param>
    /// <param name="peer">The sender.</param>
    /// <param name="data">The message.</param>
    private void OnData(object? sender, PeerId peer, ReadOnlySpan<byte> data)
        => MessageReceived?.Invoke(this, new PeerMessage(peer, data.ToArray()));
}
