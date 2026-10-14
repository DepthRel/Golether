using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Golether.Core.Media;
using Golether.Media.Streaming.Caching;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Protocol;

namespace Golether.Media.Streaming.Tests;

/// <summary>
/// Tests of the chunk protocol, the cache and the seekable media stream.
/// </summary>
public sealed class StreamingTests : IAsyncLifetime
{
    /// <summary>
    /// The chunk size used by the tests.
    /// </summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// The temporary media file.
    /// </summary>
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"golether-media-{Guid.NewGuid():N}.bin");

    /// <summary>
    /// The content of the media file.
    /// </summary>
    private byte[] _content = [];

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _content = RandomNumberGenerator.GetBytes((5 * ChunkSize) + 1234);
        await File.WriteAllBytesAsync(_path, _content);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The quick identifier is stable and depends on the content.
    /// </summary>
    [Fact]
    public async Task QuickId_IsStableAndContentDependent()
    {
        var first = await MediaFiles.DescribeAsync(_path, TestContext.Current.CancellationToken);
        var second = await MediaFiles.DescribeAsync(_path, TestContext.Current.CancellationToken);
        var changed = _content.ToArray();
        changed[^1] ^= 0xFF;
        var other = await MediaFiles.ComputeQuickIdAsync(new MemoryStream(changed), TestContext.Current.CancellationToken);

        Assert.Equal(first.QuickId, second.QuickId);
        Assert.NotEqual(first.QuickId, other);
        Assert.Equal(_content.Length, first.Length);
        Assert.Equal(Path.GetFileName(_path), first.FileName);
    }

    /// <summary>
    /// A client receives every chunk over a socket, including the short last one.
    /// </summary>
    [Fact]
    public async Task ServerAndClient_TransferAllChunks()
    {
        var media = await DescribeAsync();
        var shared = new LocalSharedMedia();
        shared.Share(_path, media);
        var (clientStream, serverStream) = await ConnectedPairAsync();
        var token = TestContext.Current.CancellationToken;
        var serving = new ChunkServer(shared).ServeAsync(serverStream, token);

        await using (var client = new ChunkClient(clientStream))
        {
            var received = new MemoryStream();
            for (var i = 0L; i < media.ChunkCount; i++)
            {
                received.Write(await client.FetchAsync(media, i, token));
            }

            Assert.Equal(_content, received.ToArray());
        }

        await serving.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    /// <summary>
    /// The host refuses requests for a file it does not share.
    /// </summary>
    [Fact]
    public async Task Server_RefusesWrongMedia()
    {
        var media = await DescribeAsync();
        var shared = new LocalSharedMedia();
        shared.Share(_path, media);
        var (clientStream, serverStream) = await ConnectedPairAsync();
        var token = TestContext.Current.CancellationToken;
        _ = new ChunkServer(shared).ServeAsync(serverStream, token);
        await using var client = new ChunkClient(clientStream);

        var foreign = media with { QuickId = new string('0', 64) };

        await Assert.ThrowsAsync<ChunkUnavailableException>(() => client.FetchAsync(foreign, 0, token));
        Assert.Equal(_content[..ChunkSize], await client.FetchAsync(media, 0, token));
    }

    /// <summary>
    /// The remote source fetches in parallel through several streams and the reader returns the exact bytes.
    /// </summary>
    [Fact]
    public async Task RemoteSourceAndReader_ReadWholeFile()
    {
        var media = await DescribeAsync();
        var shared = new LocalSharedMedia();
        shared.Share(_path, media);
        var token = TestContext.Current.CancellationToken;
        var server = new ChunkServer(shared);
        var opened = 0;

        async Task<Stream> OpenAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref opened);
            var (client, serverSide) = await ConnectedPairAsync();
            _ = server.ServeAsync(serverSide, CancellationToken.None);
            return client;
        }

        var source = new RemoteChunkSource(media, OpenAsync, parallelism: 3);
        await using var reader = new CachedMediaReader(source, new ChunkCache(), new ReadAheadOptions { ReadAheadChunks = 3, BatchSize = 3 });
        await using var stream = new MediaReadStream(reader);

        var copy = new MemoryStream();
        await stream.CopyToAsync(copy, 10_000, token);

        Assert.Equal(_content, copy.ToArray());
        Assert.InRange(opened, 1, 3);
        Assert.True(source.BytesReceived >= _content.Length);
    }

    /// <summary>
    /// A connection that opens and then never answers does not hang the film: after a short wait the piece is asked
    /// for again over a new connection. Connections made through two routers sometimes behave like this, carrying
    /// small messages and stopping dead on a large answer, with no error on either side.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task DeadConnection_IsReplacedInsteadOfWaitedFor()
    {
        var media = await DescribeAsync();
        var shared = new LocalSharedMedia();
        shared.Share(_path, media);
        var token = TestContext.Current.CancellationToken;
        var server = new ChunkServer(shared);
        var opened = 0;
        var dead = new List<Stream>();

        async Task<Stream> OpenAsync(CancellationToken ct)
        {
            var (client, serverSide) = await ConnectedPairAsync();
            if (Interlocked.Increment(ref opened) == 1)
            {
                // The first connection is open, takes the request and never answers.
                dead.Add(serverSide);
            }
            else
            {
                _ = server.ServeAsync(serverSide, CancellationToken.None);
            }

            return client;
        }

        await using var source = new RemoteChunkSource(media, OpenAsync, parallelism: 2, stallTimeout: TimeSpan.FromMilliseconds(300));

        var chunk = await source.GetChunkAsync(1, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);

        Assert.Equal(_content.AsSpan(ChunkSize, ChunkSize).ToArray(), chunk);
        Assert.Equal(2, opened);
        foreach (var stream in dead)
        {
            await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// What the player needs now is not held up by what is read ahead. A player that opens a file jumps around it
    /// (an AVI keeps its index at the end); if reading ahead took every stream to the host, the piece the player
    /// waits for would queue behind pieces nobody needs yet, which on a slow connection takes minutes.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadAhead_LeavesAStreamForWhatThePlayerNeedsNow()
    {
        var token = TestContext.Current.CancellationToken;
        const int Streams = 3;
        var media = new MediaDescriptor { FileName = "film.avi", Length = 20 * 1024, ChunkSize = 1024, QuickId = new string('a', 64) };
        await using var source = new SlowSource(media, Streams, immediate: [0, 1, 19]);
        await using var reader = new CachedMediaReader(source, new ChunkCache(), ReadAheadOptions.For(Streams, media.ChunkSize));
        var buffer = new byte[16];

        Assert.Equal(16, await reader.ReadAsync(0, buffer, token));
        Assert.Equal(16, await reader.ReadAsync(1024, buffer, token));

        // The player reads on, so reading ahead has started, and its pieces are stuck on the slow connection.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (source.Stuck < Streams - 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, token);
        }

        Assert.Equal(Streams - 1, source.Stuck);

        // The player jumps to the end of the file: that piece comes at once.
        var read = await reader.ReadAsync(19 * 1024, buffer, token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal(16, read);
        source.Release();
    }

    /// <summary>
    /// Landing somewhere in the file does not start reading ahead: a player that opens a file jumps around it, and
    /// only going on from one piece to the next shows that it plays from there.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ReadAhead_WaitsUntilThePlayerReadsOn()
    {
        var token = TestContext.Current.CancellationToken;
        var media = new MediaDescriptor { FileName = "film.avi", Length = 20 * 1024, ChunkSize = 1024, QuickId = new string('a', 64) };
        await using var source = new SlowSource(media, streams: 3, immediate: [0, 5, 6, 19]);
        await using var reader = new CachedMediaReader(source, new ChunkCache(), ReadAheadOptions.For(3, media.ChunkSize));
        var buffer = new byte[16];

        await reader.ReadAsync(0, buffer, token);
        await reader.ReadAsync(19 * 1024, buffer, token);
        await reader.ReadAsync(5 * 1024, buffer, token);
        await Task.Delay(300, token);
        Assert.Equal(0, source.Stuck);

        await reader.ReadAsync(6 * 1024, buffer, token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (source.Stuck < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, token);
        }

        Assert.Equal(2, source.Stuck);
        source.Release();
    }

    /// <summary>
    /// The settings for a number of streams: one stream is always kept free, and the distance read ahead is the
    /// same in bytes whatever the size of a piece.
    /// </summary>
    [Fact]
    public void ReadAheadOptions_FollowTheStreamsAndThePieceSize()
    {
        Assert.Equal(3, ReadAheadOptions.For(4, 1024 * 1024).BatchSize);
        Assert.Equal(1, ReadAheadOptions.For(1, 1024 * 1024).BatchSize);
        Assert.Equal(128, ReadAheadOptions.For(4, 1024 * 1024).ReadAheadChunks);
        Assert.Equal(32, ReadAheadOptions.For(4, 4 * 1024 * 1024).ReadAheadChunks);
        Assert.Equal(8, ReadAheadOptions.For(4, 16 * 1024 * 1024).ReadAheadChunks);
    }

    /// <summary>
    /// A source with a limited number of streams where every piece but a few never arrives until released.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="streams">How many pieces can be fetched at once.</param>
    /// <param name="immediate">The pieces that arrive at once.</param>
    private sealed class SlowSource(MediaDescriptor media, int streams, long[] immediate) : IChunkSource
    {
        /// <summary>
        /// The streams to the host.
        /// </summary>
        private readonly SemaphoreSlim _slots = new(streams, streams);

        /// <summary>
        /// Completed when the stuck pieces may arrive.
        /// </summary>
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// The number of pieces that hold a stream and do not arrive.
        /// </summary>
        private int _stuck;

        /// <inheritdoc />
        public MediaDescriptor Media => media;

        /// <summary>
        /// Gets the number of pieces that hold a stream and do not arrive.
        /// </summary>
        public int Stuck => Volatile.Read(ref _stuck);

        /// <summary>
        /// Lets the stuck pieces arrive.
        /// </summary>
        public void Release() => _released.TrySetResult();

        /// <inheritdoc />
        public async ValueTask<byte[]> GetChunkAsync(long index, CancellationToken cancellationToken)
        {
            await _slots.WaitAsync(cancellationToken);
            try
            {
                if (!immediate.Contains(index))
                {
                    Interlocked.Increment(ref _stuck);
                    await _released.Task.WaitAsync(cancellationToken);
                }

                return new byte[media.GetChunkLength(index)];
            }
            finally
            {
                _slots.Release();
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// The stream seeks and reads synchronously like the player does.
    /// </summary>
    [Fact]
    public async Task MediaReadStream_SeeksAndReads()
    {
        var media = await DescribeAsync();
        var source = new LocalChunkSource(media, MediaFiles.OpenRead(_path));
        await using var reader = new CachedMediaReader(source, new ChunkCache());
        using var stream = new MediaReadStream(reader);

        stream.Seek((2 * ChunkSize) - 10, SeekOrigin.Begin);
        var buffer = new byte[100];
        var read = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(10, read);
        Assert.Equal(_content.AsSpan((2 * ChunkSize) - 10, 10).ToArray(), buffer[..10]);
        Assert.Equal(2 * ChunkSize, stream.Position);
        stream.Seek(0, SeekOrigin.End);
        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
        Assert.Throws<IOException>(() => stream.Seek(1, SeekOrigin.End));
        Assert.True(reader.GetBufferedBytesAhead((2 * ChunkSize) - 10) >= 10);
    }

    /// <summary>
    /// The cache evicts the least recently used chunks above its capacity.
    /// </summary>
    [Fact]
    public void Cache_EvictsLeastRecentlyUsed()
    {
        var cache = new ChunkCache(capacity: 30);
        cache.Add(0, new byte[10]);
        cache.Add(1, new byte[10]);
        cache.Add(2, new byte[10]);
        Assert.True(cache.TryGet(0, out _));

        cache.Add(3, new byte[10]);

        Assert.True(cache.Contains(0));
        Assert.False(cache.Contains(1));
        Assert.Equal(30, cache.Size);
        Assert.Equal(2, cache.CountContiguous(2, 10));
        Assert.Equal(0, cache.CountContiguous(1, 10));
        cache.Clear();
        Assert.Equal(0, cache.Size);
    }

    /// <summary>
    /// Describes the test file with the test chunk size.
    /// </summary>
    /// <returns>The descriptor.</returns>
    private async Task<MediaDescriptor> DescribeAsync()
        => (await MediaFiles.DescribeAsync(_path, TestContext.Current.CancellationToken)) with { ChunkSize = ChunkSize };

    /// <summary>
    /// Creates two connected loopback socket streams.
    /// </summary>
    /// <returns>The client and server streams.</returns>
    private static async Task<(Stream Client, Stream Server)> ConnectedPairAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        var accepting = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var server = await accepting;
        return (client.GetStream(), server.GetStream());
    }
}
