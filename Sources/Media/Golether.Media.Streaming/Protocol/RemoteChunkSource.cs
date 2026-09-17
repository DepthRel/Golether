using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Golether.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// Provides chunks of a media file.
/// </summary>
public interface IChunkSource : IAsyncDisposable
{
    /// <summary>
    /// Gets the media descriptor.
    /// </summary>
    MediaDescriptor Media { get; }

    /// <summary>
    /// Returns a chunk.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verified chunk data.</returns>
    /// <exception cref="ChunkUnavailableException">The chunk cannot be obtained.</exception>
    ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken);
}

/// <summary>
/// A chunk cannot be obtained from the source.
/// </summary>
public sealed class ChunkUnavailableException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ChunkUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Requests chunks over one data stream, one request at a time.
/// </summary>
public sealed class ChunkClient : IAsyncDisposable
{
    /// <summary>
    /// The data stream.
    /// </summary>
    private readonly Stream _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkClient"/> class.
    /// </summary>
    /// <param name="stream">The authenticated data stream; ownership is transferred.</param>
    public ChunkClient(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Fetches and verifies a chunk.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chunk data.</returns>
    /// <exception cref="ChunkUnavailableException">The host refused the request.</exception>
    /// <exception cref="InvalidDataException">The response is inconsistent or corrupted.</exception>
    /// <exception cref="IOException">The stream failed.</exception>
    public async Task<byte[]> FetchAsync(MediaDescriptor media, long index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        var expectedLength = media.GetChunkLength(index);
        var request = new byte[ChunkProtocol.RequestSize];
        ChunkProtocol.WriteRequest(request, index, ChunkProtocol.GetMediaKey(media));
        await _stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var header = new byte[ChunkProtocol.ResponseHeaderSize];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var status = (ChunkStatus)header[0];
        var answeredIndex = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(1));
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(9));
        if (answeredIndex != index)
        {
            throw new InvalidDataException($"The host answered chunk {answeredIndex} instead of {index}.");
        }

        if (status != ChunkStatus.Ok)
        {
            throw new ChunkUnavailableException($"The host refused chunk {index}: {status}.");
        }

        if (length != expectedLength)
        {
            throw new InvalidDataException($"Chunk {index} has {length} bytes instead of {expectedLength}.");
        }

        var data = new byte[length];
        await _stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        if (!SHA256.HashData(data).AsSpan().SequenceEqual(header.AsSpan(13, 32)))
        {
            throw new InvalidDataException($"Chunk {index} is corrupted.");
        }

        return data;
    }

    /// <summary>
    /// Closes the stream.
    /// </summary>
    /// <returns>A task that completes when the stream is closed.</returns>
    /// <summary>
    /// Asks the host for the SHA-256 of a chunk.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The 32-byte hash.</returns>
    /// <exception cref="ChunkUnavailableException">The host refused.</exception>
    /// <exception cref="InvalidDataException">The answer is invalid.</exception>
    public async Task<byte[]> FetchHashAsync(MediaDescriptor media, long index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        var request = new byte[ChunkProtocol.RequestSize];
        ChunkProtocol.WriteRequest(request, index, ChunkProtocol.GetMediaKey(media), ChunkProtocol.GetHash);
        await _stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var header = new byte[ChunkProtocol.ResponseHeaderSize];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var status = (ChunkStatus)header[0];
        if (BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(1)) != index)
        {
            throw new InvalidDataException($"The host answered the hash of another chunk than {index}.");
        }

        if (status != ChunkStatus.Ok)
        {
            throw new ChunkUnavailableException($"The host refused the hash of chunk {index}: {status}.");
        }

        if (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(9)) != media.GetChunkLength(index))
        {
            throw new InvalidDataException($"The host reported a wrong length for chunk {index}.");
        }

        return header.AsSpan(13, 32).ToArray();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}

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

/// <summary>
/// <see cref="IChunkSource"/> over a local file, used when a participant has the same file or in tests.
/// </summary>
public sealed class LocalChunkSource : IChunkSource
{
    /// <summary>
    /// The file.
    /// </summary>
    private readonly Stream _file;

    /// <summary>
    /// Serializes reads.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalChunkSource"/> class.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="file">A readable, seekable stream; ownership is transferred.</param>
    public LocalChunkSource(MediaDescriptor media, Stream file)
    {
        Media = media ?? throw new ArgumentNullException(nameof(media));
        _file = file ?? throw new ArgumentNullException(nameof(file));
    }

    /// <inheritdoc />
    public MediaDescriptor Media { get; }

    /// <inheritdoc />
    public async ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
    {
        var data = new byte[Media.GetChunkLength(index)];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _file.Position = index * Media.ChunkSize;
            await _file.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            return data;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _file.DisposeAsync();
}
