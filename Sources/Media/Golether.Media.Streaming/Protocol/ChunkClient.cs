using System.Buffers.Binary;
using System.Security.Cryptography;
using Golether.Core.Data.Enums;
using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

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
        : this(stream, DefaultStallTimeout)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkClient"/> class with its own patience.
    /// </summary>
    /// <param name="stream">The stream to the host; ownership is transferred.</param>
    /// <param name="stallTimeout">How long the answer may stand still before the stream is given up on.</param>
    public ChunkClient(Stream stream, TimeSpan stallTimeout)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stallTimeout, TimeSpan.Zero);
        _stallTimeout = stallTimeout;
    }

    /// <summary>
    /// How long an answer may stand still by default. A connection made through two routers sometimes carries small
    /// messages and then stops dead on a large answer: nothing fails, the bytes simply never come. Waiting for it is
    /// pointless; the piece is asked for again over a new connection.
    /// </summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long the answer may stand still before the stream is given up on.
    /// </summary>
    private readonly TimeSpan _stallTimeout;

    /// <summary>
    /// Fills a buffer from the stream; gives up when no byte arrives for <see cref="_stallTimeout"/>.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the buffer is full.</returns>
    /// <exception cref="IOException">The stream ended or stood still for too long.</exception>
    private async Task ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (!buffer.IsEmpty)
        {
            patience.CancelAfter(_stallTimeout);
            int read;
            try
            {
                read = await _stream.ReadAsync(buffer, patience.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException($"The host sent nothing for {_stallTimeout.TotalSeconds:0} s.");
            }

            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            buffer = buffer[read..];
        }
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
        await ReadAsync(header, cancellationToken).ConfigureAwait(false);
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
        await ReadAsync(data, cancellationToken).ConfigureAwait(false);
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
        await ReadAsync(header, cancellationToken).ConfigureAwait(false);
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
    public ValueTask DisposeAsync() => _detached ? ValueTask.CompletedTask : _stream.DisposeAsync();

    /// <summary>
    /// Whether the stream was handed over with <see cref="Detach"/>.
    /// </summary>
    private bool _detached;

    /// <summary>
    /// Gives the stream back to the caller, who owns it from now on. Call only between requests: a stream in the
    /// middle of an answer is of no use to anybody else.
    /// </summary>
    /// <returns>The stream.</returns>
    public Stream Detach()
    {
        _detached = true;
        return _stream;
    }
}
