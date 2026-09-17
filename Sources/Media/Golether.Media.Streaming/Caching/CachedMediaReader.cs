using Golether.Core.Media;
using Golether.Media.Streaming.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Streaming.Caching;

/// <summary>
/// Settings of read-ahead.
/// </summary>
public sealed record ReadAheadOptions
{
    /// <summary>
    /// Gets the number of chunks fetched ahead of the read position (default 32, that is 128 MiB with 4 MiB chunks).
    /// </summary>
    public int ReadAheadChunks { get; init; } = 32;

    /// <summary>
    /// Gets the number of chunks fetched at once by the read-ahead loop (default 4).
    /// </summary>
    public int BatchSize { get; init; } = 4;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ReadAheadChunks);
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1);
    }
}

/// <summary>
/// Reads media bytes through the chunk cache and fetches chunks ahead of the read position in the background.
/// </summary>
public sealed class CachedMediaReader : IAsyncDisposable
{
    /// <summary>
    /// The chunk source.
    /// </summary>
    private readonly IChunkSource _source;

    /// <summary>
    /// The cache.
    /// </summary>
    private readonly ChunkCache _cache;

    /// <summary>
    /// The read-ahead options.
    /// </summary>
    private readonly ReadAheadOptions _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Wakes the read-ahead loop.
    /// </summary>
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);

    /// <summary>
    /// Stops the read-ahead loop.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// The read-ahead loop.
    /// </summary>
    private readonly Task _readAhead;

    /// <summary>
    /// The chunk index of the latest read.
    /// </summary>
    private long _currentChunk;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedMediaReader"/> class.
    /// </summary>
    /// <param name="source">The chunk source.</param>
    /// <param name="cache">The cache.</param>
    /// <param name="options">The read-ahead options.</param>
    /// <param name="logger">The logger.</param>
    public CachedMediaReader(IChunkSource source, ChunkCache cache, ReadAheadOptions? options = null, ILogger? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? new ReadAheadOptions();
        _options.Validate();
        _logger = logger ?? NullLogger.Instance;
        _readAhead = Task.Run(() => ReadAheadLoopAsync(_stopping.Token));
    }

    /// <summary>
    /// Gets the media descriptor.
    /// </summary>
    public MediaDescriptor Media => _source.Media;

    /// <summary>
    /// Reads bytes at a position.
    /// </summary>
    /// <param name="position">The byte position.</param>
    /// <param name="buffer">The destination.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of bytes read; 0 at the end of the file.</returns>
    public async ValueTask<int> ReadAsync(long position, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (position >= Media.Length || buffer.IsEmpty)
        {
            return 0;
        }

        var index = position / Media.ChunkSize;
        var chunk = await GetChunkAsync(index, cancellationToken).ConfigureAwait(false);
        var offset = (int)(position - (index * Media.ChunkSize));
        var count = Math.Min(buffer.Length, chunk.Length - offset);
        chunk.AsMemory(offset, count).CopyTo(buffer);

        if (Interlocked.Exchange(ref _currentChunk, index) != index)
        {
            _wake.Release();
        }

        return count;
    }

    /// <summary>
    /// Returns the number of bytes cached contiguously from a position (within the read-ahead window).
    /// </summary>
    /// <param name="position">The byte position.</param>
    /// <returns>The number of bytes.</returns>
    public long GetBufferedBytesAhead(long position)
    {
        if (position >= Media.Length)
        {
            return 0;
        }

        var index = position / Media.ChunkSize;
        var chunks = _cache.CountContiguous(index, _options.ReadAheadChunks + 1L);
        if (chunks == 0)
        {
            return 0;
        }

        var end = Math.Min(Media.Length, (index + chunks) * Media.ChunkSize);
        return end - position;
    }

    /// <summary>
    /// Stops the read-ahead loop and releases the source.
    /// </summary>
    /// <returns>A task that completes when the reader is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _readAhead.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _source.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }

    /// <summary>
    /// Returns a chunk from the cache or the source.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chunk data.</returns>
    private async ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(index, out var cached))
        {
            return cached;
        }

        var data = await _source.GetChunkAsync(index, cancellationToken).ConfigureAwait(false);
        _cache.Add(index, data);
        return data;
    }

    /// <summary>
    /// Fetches chunks ahead of the latest read position.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private async Task ReadAheadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _wake.WaitAsync(cancellationToken).ConfigureAwait(false);
            var origin = Interlocked.Read(ref _currentChunk);
            var last = Math.Min(Media.ChunkCount - 1, origin + _options.ReadAheadChunks);
            var next = origin + 1;
            while (next <= last && Interlocked.Read(ref _currentChunk) == origin && !cancellationToken.IsCancellationRequested)
            {
                var batch = new List<Task>(_options.BatchSize);
                for (; next <= last && batch.Count < _options.BatchSize; next++)
                {
                    if (!_cache.Contains(next))
                    {
                        batch.Add(GetChunkAsync(next, cancellationToken).AsTask());
                    }
                }

                try
                {
                    await Task.WhenAll(batch).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException)
                {
                    _logger.LogWarning("Read-ahead stopped: {Reason}", ex.Message);
                    break;
                }
            }
        }
    }
}
