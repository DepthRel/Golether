using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Data.Enums;
using Golether.Core.Media;

namespace Golether.Media.Streaming.Protocol;

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
    /// The files shared next to the media (the pictures of a shown folder), or <see langword="null"/>.
    /// </summary>
    private readonly ISharedFileSet? _files;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<ChunkServer> _logger;

    /// <summary>
    /// Guards the switch of <see cref="_hashes"/> to another media. The dictionary itself cannot be the lock: it is
    /// replaced inside it, and two threads would then hold two different locks.
    /// </summary>
    private readonly Lock _hashGate = new();

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
    /// <param name="files">The files shared next to the media, or <see langword="null"/>.</param>
    public ChunkServer(ISharedMedia media, ILogger<ChunkServer>? logger = null, ISharedFileSet? files = null)
    {
        _hashes = new ConcurrentDictionary<long, byte[]>();
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _files = files;
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
        var request = new byte[ChunkProtocol.REQUEST_SIZE];
        var header = new byte[ChunkProtocol.RESPONSE_HEADER_SIZE];
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

                MediaDescriptor media;
                Func<Stream> open;
                var isMain = false;
                if (_media.Descriptor is { } main && ChunkProtocol.GetMediaKey(main) == key)
                {
                    media = main;
                    open = _media.OpenRead;
                    isMain = true;
                }
                else if (_files is not null && _files.TryFind(key, out var shared, out var openShared))
                {
                    // A picture is fetched whole by one participant and never checked against hashes by the
                    // others: its hashes are not kept, so the hashes of the film are not thrown away for it.
                    media = shared;
                    open = openShared;
                }
                else
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

                var hashes = isMain ? HashesFor(key) : null;
                if (operation == ChunkProtocol.GET_HASH && hashes is not null && hashes.TryGetValue(index, out var known))
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

                    file = open();
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
                if (hashes is not null)
                {
                    hashes[index] = hash;
                }

                ChunkProtocol.WriteResponseHeader(header, ChunkStatus.Ok, index, length, hash);
                if (operation == ChunkProtocol.GET_HASH)
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
        lock (_hashGate)
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
