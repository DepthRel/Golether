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
