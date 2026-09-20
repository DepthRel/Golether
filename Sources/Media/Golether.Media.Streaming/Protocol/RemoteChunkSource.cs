using System.Collections.Concurrent;
using Golether.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// Fetches chunks from the host over several parallel data streams, deduplicating concurrent requests.
/// </summary>
public sealed class RemoteChunkSource : IChunkSource
{
    /// <summary>
    /// The number of attempts per chunk.
    /// </summary>
    private const int Attempts = 3;

    /// <summary>
    /// Opens a new data stream to the host.
    /// </summary>
    private readonly Func<CancellationToken, Task<Stream>> _openStream;

    /// <summary>
    /// Idle clients.
    /// </summary>
    private readonly ConcurrentBag<ChunkClient> _idle = [];

    /// <summary>
    /// Limits the number of clients in use.
    /// </summary>
    private readonly SemaphoreSlim _slots;

    /// <summary>
    /// Requests in flight by chunk index.
    /// </summary>
    private readonly ConcurrentDictionary<long, Lazy<Task<byte[]>>> _inFlight = new();

    /// <summary>
    /// Cancels requests on disposal.
    /// </summary>
    private readonly CancellationTokenSource _disposing = new();

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<RemoteChunkSource> _logger;

    /// <summary>
    /// The total number of bytes received.
    /// </summary>
    private long _bytesReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteChunkSource"/> class.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="openStream">Opens a new authenticated data stream to the host.</param>
    /// <param name="parallelism">The number of parallel data streams.</param>
    /// <param name="logger">The logger.</param>
    public RemoteChunkSource(MediaDescriptor media, Func<CancellationToken, Task<Stream>> openStream, int parallelism, ILogger<RemoteChunkSource>? logger = null)
    {
        Media = media ?? throw new ArgumentNullException(nameof(media));
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parallelism, 16);
        _slots = new SemaphoreSlim(parallelism, parallelism);
        _logger = logger ?? NullLogger<RemoteChunkSource>.Instance;
    }

    /// <inheritdoc />
    public MediaDescriptor Media { get; }

    /// <summary>
    /// Gets the total number of bytes received.
    /// </summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    /// <inheritdoc />
    public ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Media.ChunkCount);
        var lazy = _inFlight.GetOrAdd(index, i => new Lazy<Task<byte[]>>(() => FetchWithRetriesAsync(i)));
        return new ValueTask<byte[]>(lazy.Value.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _disposing.CancelAsync().ConfigureAwait(false);
        while (_idle.TryTake(out var client))
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the SHA-256 of a chunk from the host without downloading it.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 32-byte hash.</returns>
    /// <exception cref="ChunkUnavailableException">The hash could not be received.</exception>
    public async Task<byte[]> GetHashAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Media.ChunkCount);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposing.Token);
        var token = linked.Token;
        await _slots.WaitAsync(token).ConfigureAwait(false);
        ChunkClient? client = null;
        try
        {
            client = _idle.TryTake(out var idle) ? idle : new ChunkClient(await _openStream(token).ConfigureAwait(false));
            var hash = await client.FetchHashAsync(Media, index, token).ConfigureAwait(false);
            _idle.Add(client);
            client = null;
            return hash;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException && ex is not ChunkUnavailableException)
        {
            throw new ChunkUnavailableException($"The hash of chunk {index} could not be received: {ex.Message}", ex);
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            _slots.Release();
        }
    }

    /// <summary>
    /// Fetches a chunk, replacing broken streams.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The chunk data.</returns>
    private async Task<byte[]> FetchWithRetriesAsync(long index)
    {
        var token = _disposing.Token;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await _slots.WaitAsync(token).ConfigureAwait(false);
                ChunkClient? client = null;
                try
                {
                    client = _idle.TryTake(out var idle) ? idle : new ChunkClient(await _openStream(token).ConfigureAwait(false));
                    var data = await client.FetchAsync(Media, index, token).ConfigureAwait(false);
                    _idle.Add(client);
                    client = null;
                    Interlocked.Add(ref _bytesReceived, data.Length);
                    return data;
                }
                catch (ChunkUnavailableException)
                {
                    if (client is not null)
                    {
                        _idle.Add(client);
                        client = null;
                    }

                    throw;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException && attempt < Attempts && !token.IsCancellationRequested)
                {
                    _logger.LogWarning("Chunk {Index}, attempt {Attempt}: {Reason}", index, attempt, ex.Message);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    throw new ChunkUnavailableException($"Chunk {index} could not be received: {ex.Message}", ex);
                }
                finally
                {
                    if (client is not null)
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                    }

                    _slots.Release();
                }
            }
        }
        finally
        {
            _inFlight.TryRemove(index, out _);
        }
    }
}
