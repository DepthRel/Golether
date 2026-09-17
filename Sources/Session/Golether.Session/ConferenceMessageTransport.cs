using Golether.Core.Identity;
using Golether.Core.Media;
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

/// <summary>
/// Chunk sharing of a participant: the exchange and what it needs to live.
/// </summary>
internal sealed class ChunkSharing : IAsyncDisposable
{
    /// <summary>
    /// The transport.
    /// </summary>
    private readonly ConferenceMessageTransport _transport;

    /// <summary>
    /// A local copy of the file served to others, or <see langword="null"/>.
    /// </summary>
    private readonly Stream? _file;

    /// <summary>
    /// Serializes reads of <see cref="_file"/>.
    /// </summary>
    private readonly Lock _fileGate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkSharing"/> class that serves a local copy.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="conference">The conferencing backend.</param>
    /// <param name="file">The local copy; the sharing owns it.</param>
    /// <param name="logger">The logger.</param>
    public ChunkSharing(MediaDescriptor media, IConferenceMedia conference, Stream file, Microsoft.Extensions.Logging.ILogger logger)
    {
        _transport = new ConferenceMessageTransport(conference);
        _file = file;
        Exchange = new PeerChunkExchange(media, _transport, index => ReadChunk(media, index), _ => [(0, media.ChunkCount)], logger: logger);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkSharing"/> class that serves cached chunks.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="conference">The conferencing backend.</param>
    /// <param name="cache">The chunk cache.</param>
    /// <param name="logger">The logger.</param>
    public ChunkSharing(MediaDescriptor media, IConferenceMedia conference, Media.Streaming.Caching.ChunkCache cache, Microsoft.Extensions.Logging.ILogger logger)
    {
        _transport = new ConferenceMessageTransport(conference);
        Exchange = new PeerChunkExchange(media, _transport, cache.Peek, cache.GetRanges, logger: logger);
    }

    /// <summary>
    /// Gets the exchange.
    /// </summary>
    public PeerChunkExchange Exchange { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Exchange.DisposeAsync().ConfigureAwait(false);
        _transport.Dispose();
        if (_file is not null)
        {
            await _file.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads a chunk of the local copy.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="index">The chunk index.</param>
    /// <returns>The chunk, or <see langword="null"/> when it cannot be read.</returns>
    private byte[]? ReadChunk(MediaDescriptor media, long index)
    {
        var data = new byte[media.GetChunkLength(index)];
        lock (_fileGate)
        {
            try
            {
                _file!.Position = index * media.ChunkSize;
                _file.ReadExactly(data);
                return data;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return null;
            }
        }
    }
}
