using System.Collections.Concurrent;
using System.Security.Cryptography;
using Golether.Core.Media;
using Golether.Media.Streaming.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Streaming.Sharing;

/// <summary>
/// Gets chunks from other participants when they have them and from the host otherwise.
/// </summary>
/// <remarks>
/// A chunk from a participant is accepted only when its SHA-256 matches the hash the host reports, so a participant
/// cannot slip in other data. Any failure falls back to the host.
/// </remarks>
public sealed class SwarmChunkSource : IChunkSource
{
    /// <summary>
    /// The host.
    /// </summary>
    private readonly RemoteChunkSource _host;

    /// <summary>
    /// The other participants.
    /// </summary>
    private readonly PeerChunkExchange _peers;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Requests in flight by chunk index.
    /// </summary>
    private readonly ConcurrentDictionary<long, Lazy<Task<byte[]>>> _inFlight = new();

    /// <summary>
    /// Backing field of <see cref="BytesFromPeers"/>.
    /// </summary>
    private long _bytesFromPeers;

    /// <summary>
    /// Backing field of <see cref="RejectedChunks"/>.
    /// </summary>
    private long _rejectedChunks;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwarmChunkSource"/> class.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="peers">The other participants.</param>
    /// <param name="logger">The logger.</param>
    public SwarmChunkSource(RemoteChunkSource host, PeerChunkExchange peers, ILogger? logger = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public MediaDescriptor Media => _host.Media;

    /// <summary>
    /// Gets the checked chunk bytes that came from participants.
    /// </summary>
    public long BytesFromPeers => Interlocked.Read(ref _bytesFromPeers);

    /// <summary>
    /// Gets the chunk bytes that came from the host.
    /// </summary>
    public long BytesFromHost => _host.BytesReceived;

    /// <summary>
    /// Gets the number of chunks from participants that failed the hash check.
    /// </summary>
    public long RejectedChunks => Interlocked.Read(ref _rejectedChunks);

    /// <inheritdoc />
    public ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Media.ChunkCount);
        var lazy = _inFlight.GetOrAdd(index, i => new Lazy<Task<byte[]>>(() => FetchAsync(i)));
        return new ValueTask<byte[]>(lazy.Value.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _peers.DisposeAsync().ConfigureAwait(false);
        await _host.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches a chunk from a participant or the host.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The chunk.</returns>
    private async Task<byte[]> FetchAsync(long index)
    {
        try
        {
            if (_peers.PeersHaving(index).Count > 0)
            {
                var fromPeer = await TryPeersAsync(index).ConfigureAwait(false);
                if (fromPeer is not null)
                {
                    return fromPeer;
                }
            }

            return await _host.GetChunkAsync(index, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(index, out _);
        }
    }

    /// <summary>
    /// Gets a chunk from a participant and checks it against the host's hash.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The checked chunk, or <see langword="null"/>.</returns>
    private async Task<byte[]?> TryPeersAsync(long index)
    {
        try
        {
            var hashTask = _host.GetHashAsync(index, CancellationToken.None);
            var received = await _peers.TryFetchAsync(index, CancellationToken.None).ConfigureAwait(false);
            if (received is not { } result)
            {
                _ = hashTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                return null;
            }

            var expected = await hashTask.ConfigureAwait(false);
            if (result.Data.Length != Media.GetChunkLength(index)
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(result.Data), expected))
            {
                Interlocked.Increment(ref _rejectedChunks);
                _logger.LogWarning("Chunk {Index} from {Peer} does not match the host's hash; using the host", index, result.Peer.ToShortString());
                return null;
            }

            Interlocked.Add(ref _bytesFromPeers, result.Data.Length);
            return result.Data;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
        {
            _logger.LogDebug("Chunk {Index} not taken from participants: {Reason}", index, ex.Message);
            return null;
        }
    }
}
