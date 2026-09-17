using System.Collections.Concurrent;
using System.Security.Cryptography;
using Golether.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Streaming.Protocol;

/// <summary>
/// The media file the host currently shares.
/// </summary>
public interface ISharedMedia
{
    /// <summary>
    /// Gets the descriptor, or <see langword="null"/> when nothing is shared.
    /// </summary>
    MediaDescriptor? Descriptor { get; }

    /// <summary>
    /// Opens the shared file for reading.
    /// </summary>
    /// <returns>A readable, seekable stream; the caller owns it.</returns>
    Stream OpenRead();
}

/// <summary>
/// Serves chunks of the shared media on a data stream.
/// </summary>
public sealed class ChunkServer
{
    /// <summary>
    /// The shared media.
    /// </summary>
    private readonly ISharedMedia _media;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ChunkServer> _logger;

    /// <summary>
    /// The hashes computed for <see cref="_hashesFor"/>, by chunk index.
    /// </summary>
    private ConcurrentDictionary<long, byte[]> _hashes;

    /// <summary>
    /// The media key the hashes belong to.
    /// </summary>
    private ulong _hashesFor;

    /// <summary>
    /// Gets the number of full chunks sent.
    /// </summary>
    public long ChunksServed => Interlocked.Read(ref _chunksServed);

    /// <summary>
    /// Gets the number of hash-only answers sent.
    /// </summary>
    public long HashesServed => Interlocked.Read(ref _hashesServed);

    /// <summary>
    /// Backing field of <see cref="ChunksServed"/>.
    /// </summary>
    private long _chunksServed;

    /// <summary>
    /// Backing field of <see cref="HashesServed"/>.
    /// </summary>
    private long _hashesServed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkServer"/> class.
    /// </summary>
    /// <param name="media">The shared media.</param>
    /// <param name="logger">The logger.</param>
    public ChunkServer(ISharedMedia media, ILogger<ChunkServer>? logger = null)
    {
        _hashes = new ConcurrentDictionary<long, byte[]>();
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _logger = logger ?? NullLogger<ChunkServer>.Instance;
    }

    /// <summary>
    /// Answers requests until the peer closes the stream.
    /// </summary>
    /// <param name="stream">The authenticated data stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the stream ends.</returns>
    /// <exception cref="InvalidDataException">The peer sent an unknown operation.</exception>
    public async Task ServeAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var request = new byte[ChunkProtocol.RequestSize];
        var header = new byte[ChunkProtocol.ResponseHeaderSize];
        byte[]? buffer = null;
        Stream? file = null;
        MediaDescriptor? openedFor = null;
        try
        {
            while (true)
            {
                var read = await stream.ReadAtLeastAsync(request, request.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                if (read < request.Length || !ChunkProtocol.TryReadRequest(request, out var operation, out var index, out var key))
                {
                    throw new InvalidDataException("The peer sent an invalid chunk request.");
                }

                var media = _media.Descriptor;
                if (media is null || ChunkProtocol.GetMediaKey(media) != key)
                {
                    ChunkProtocol.WriteResponseHeader(header, ChunkStatus.WrongMedia, index, 0, default);
                    await WriteAsync(stream, header, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (index < 0 || index >= media.ChunkCount)
                {
                    ChunkProtocol.WriteResponseHeader(header, ChunkStatus.OutOfRange, index, 0, default);
                    await WriteAsync(stream, header, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var hashes = HashesFor(key);
                if (operation == ChunkProtocol.GetHash && hashes.TryGetValue(index, out var known))
                {
                    ChunkProtocol.WriteResponseHeader(header, ChunkStatus.Ok, index, media.GetChunkLength(index), known);
                    await WriteAsync(stream, header, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _hashesServed);
                    continue;
                }

                if (!ReferenceEquals(openedFor, media))
                {
                    if (file is not null)
                    {
                        await file.DisposeAsync().ConfigureAwait(false);
                    }

                    file = _media.OpenRead();
                    openedFor = media;
                    buffer = new byte[media.ChunkSize];
                }

                var length = media.GetChunkLength(index);
                try
                {
                    file!.Position = index * media.ChunkSize;
                    await file.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Reading chunk {Index} failed", index);
                    ChunkProtocol.WriteResponseHeader(header, ChunkStatus.ReadError, index, 0, default);
                    await WriteAsync(stream, header, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var payload = buffer.AsMemory(0, length);
                var hash = SHA256.HashData(payload.Span);
                hashes[index] = hash;
                ChunkProtocol.WriteResponseHeader(header, ChunkStatus.Ok, index, length, hash);
                if (operation == ChunkProtocol.GetHash)
                {
                    await WriteAsync(stream, header, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _hashesServed);
                    continue;
                }

                await WriteAsync(stream, header, payload, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _chunksServed);
            }
        }
        finally
        {
            if (file is not null)
            {
                await file.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Returns the hash cache of a media, replacing the cache of another media.
    /// </summary>
    /// <param name="key">The media key.</param>
    /// <returns>The cache.</returns>
    private ConcurrentDictionary<long, byte[]> HashesFor(ulong key)
    {
        lock (_hashes)
        {
            if (_hashesFor != key)
            {
                _hashes = new ConcurrentDictionary<long, byte[]>();
                _hashesFor = key;
            }

            return _hashes;
        }
    }

    /// <summary>
    /// Writes a response.
    /// </summary>
    /// <param name="stream">The data stream.</param>
    /// <param name="header">The response header.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the response is flushed.</returns>
    private static async Task WriteAsync(Stream stream, byte[] header, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// <see cref="ISharedMedia"/> for a local file.
/// </summary>
public sealed class LocalSharedMedia : ISharedMedia
{
    /// <summary>
    /// The shared file and its descriptor.
    /// </summary>
    private volatile SharedFile? _current;

    /// <inheritdoc />
    public MediaDescriptor? Descriptor => _current?.Descriptor;

    /// <summary>
    /// Gets the path of the shared file, or <see langword="null"/>.
    /// </summary>
    public string? FilePath => _current?.Path;

    /// <summary>
    /// Shares a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="descriptor">The descriptor of the file.</param>
    public void Share(string path, MediaDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(descriptor);
        _current = new SharedFile(Path.GetFullPath(path), descriptor);
    }

    /// <summary>
    /// Stops sharing.
    /// </summary>
    public void Clear() => _current = null;

    /// <inheritdoc />
    public Stream OpenRead()
    {
        var current = _current ?? throw new InvalidOperationException("No media is shared.");
        return Files.MediaFiles.OpenRead(current.Path);
    }

    /// <summary>
    /// A shared file.
    /// </summary>
    /// <param name="Path">The full path.</param>
    /// <param name="Descriptor">The descriptor.</param>
    private sealed record SharedFile(string Path, MediaDescriptor Descriptor);
}
