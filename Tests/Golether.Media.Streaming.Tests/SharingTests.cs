using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Media.Streaming.Caching;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Protocol;
using Golether.Media.Streaming.Sharing;

namespace Golether.Media.Streaming.Tests;

/// <summary>
/// Tests of chunk sharing between participants: hash-only answers of the host, the peer exchange and the swarm source.
/// </summary>
public sealed class SharingTests : IAsyncLifetime
{
    /// <summary>
    /// The chunk size used by the tests.
    /// </summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// The first participant.
    /// </summary>
    private static readonly PeerId Anna = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// The second participant.
    /// </summary>
    private static readonly PeerId Boris = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The temporary media file.
    /// </summary>
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"golether-share-{Guid.NewGuid():N}.bin");

    /// <summary>
    /// The content of the media file.
    /// </summary>
    private byte[] _content = [];

    /// <summary>
    /// The media.
    /// </summary>
    private MediaDescriptor _media = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _content = RandomNumberGenerator.GetBytes((5 * ChunkSize) + 1234);
        await File.WriteAllBytesAsync(_path, _content);
        _media = (await MediaFiles.DescribeAsync(_path, CancellationToken.None)) with { ChunkSize = ChunkSize };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The host answers a hash request without the chunk and remembers hashes.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Host_AnswersHashOnly()
    {
        var token = TestContext.Current.CancellationToken;
        var (server, source) = CreateHost();
        await using var _ = source;

        var hash = await source.GetHashAsync(5, token);

        Assert.Equal(SHA256.HashData(Chunk(5)), hash);
        Assert.Equal(0, source.BytesReceived);
        Assert.Equal(0, server.ChunksServed);
        Assert.Equal(1, server.HashesServed);
        await source.GetChunkAsync(2, token);
        Assert.Equal(SHA256.HashData(Chunk(2)), await source.GetHashAsync(2, token));
        Assert.Equal(1, server.ChunksServed);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => source.GetHashAsync(6, token));
    }

    /// <summary>
    /// A participant gets a chunk from another participant that announced it, in parts, and gets nothing for chunks
    /// the other one does not have.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Exchange_TransfersAnnouncedChunks()
    {
        var token = TestContext.Current.CancellationToken;
        var network = new MemoryNetwork();
        var annaCache = new ChunkCache();
        annaCache.Add(1, Chunk(1));
        annaCache.Add(2, Chunk(2));
        annaCache.Add(5, Chunk(5));
        await using var anna = Exchange(network.Join(Anna), annaCache);
        await using var boris = Exchange(network.Join(Boris), new ChunkCache());

        anna.Announce();
        Assert.Equal([Anna], boris.PeersHaving(2));
        Assert.Empty(boris.PeersHaving(3));
        Assert.Equal([(1L, 2L), (5L, 1L)], annaCache.GetRanges(8));

        var received = await boris.TryFetchAsync(2, token);
        Assert.NotNull(received);
        Assert.Equal(Anna, received.Value.Peer);
        Assert.Equal(Chunk(2), received.Value.Data);
        Assert.Equal(Chunk(5), (await boris.TryFetchAsync(5, token))!.Value.Data);
        Assert.Equal(ChunkSize + 1234, anna.BytesSent);
        Assert.True(network.Messages > 8, "The chunk travels in several parts.");

        // A peer that claims a chunk it no longer has answers "miss"; nobody else has it.
        annaCache.Clear();
        Assert.Null(await boris.TryFetchAsync(1, token));
        Assert.Null(await boris.TryFetchAsync(3, token));
    }

    /// <summary>
    /// The swarm source takes chunks from participants after checking them with the host's hash, and falls back to the
    /// host when a participant sends wrong data or disappears.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Swarm_ChecksPeersAndFallsBack()
    {
        var token = TestContext.Current.CancellationToken;
        var network = new MemoryNetwork();
        var annaCache = new ChunkCache();
        for (var i = 0; i < _media.ChunkCount; i++)
        {
            annaCache.Add(i, Chunk(i));
        }

        await using var anna = Exchange(network.Join(Anna), annaCache);
        var (server, host) = CreateHost();
        var borisExchange = Exchange(network.Join(Boris), new ChunkCache());
        await using var swarm = new SwarmChunkSource(host, borisExchange);
        anna.Announce();

        Assert.Equal(Chunk(0), await swarm.GetChunkAsync(0, token));
        Assert.Equal(Chunk(5), await swarm.GetChunkAsync(5, token));
        Assert.Equal(0, server.ChunksServed);
        Assert.Equal(2, server.HashesServed);
        Assert.Equal(ChunkSize + 1234, swarm.BytesFromPeers);

        network.Corrupt = true;
        Assert.Equal(Chunk(1), await swarm.GetChunkAsync(1, token));
        Assert.Equal(1, swarm.RejectedChunks);
        Assert.Equal(1, server.ChunksServed);

        network.Corrupt = false;
        network.Leave(Anna);
        Assert.Equal(Chunk(2), await swarm.GetChunkAsync(2, token));
        Assert.Equal(2, server.ChunksServed);
    }

    /// <summary>
    /// A full channel buffer only delays the upload.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Exchange_WaitsForBuffer()
    {
        var network = new MemoryNetwork { RefuseEvery = 3 };
        var annaCache = new ChunkCache();
        annaCache.Add(0, Chunk(0));
        await using var anna = Exchange(network.Join(Anna), annaCache);
        await using var boris = Exchange(network.Join(Boris), new ChunkCache());
        anna.Announce();
        if (boris.PeersHaving(0).Count == 0)
        {
            anna.Announce();
        }

        var received = await boris.TryFetchAsync(0, TestContext.Current.CancellationToken);

        Assert.Equal(Chunk(0), received?.Data);
        Assert.True(network.Refused > 0);
    }

    /// <summary>
    /// Returns a chunk of the test file.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The bytes.</returns>
    private byte[] Chunk(long index) => _content.AsSpan((int)(index * ChunkSize), _media.GetChunkLength(index)).ToArray();

    /// <summary>
    /// Creates an exchange serving from a cache.
    /// </summary>
    /// <param name="transport">The transport.</param>
    /// <param name="cache">The cache.</param>
    /// <returns>The exchange.</returns>
    private PeerChunkExchange Exchange(IPeerMessageTransport transport, ChunkCache cache)
        => new(_media, transport, cache.Peek, cache.GetRanges, new PeerChunkExchangeOptions
        {
            FragmentSize = 8 * 1024,
            AnnounceInterval = TimeSpan.FromHours(1),
            StallTimeout = TimeSpan.FromSeconds(2),
        });

    /// <summary>
    /// Creates the host server and a remote source connected to it over loopback.
    /// </summary>
    /// <returns>The server and the source.</returns>
    private (ChunkServer Server, RemoteChunkSource Source) CreateHost()
    {
        var shared = new LocalSharedMedia();
        shared.Share(_path, _media);
        var server = new ChunkServer(shared);

        async Task<Stream> OpenAsync(CancellationToken cancellationToken)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            var accepting = listener.AcceptTcpClientAsync(cancellationToken);
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
            var serverSide = await accepting;
            _ = server.ServeAsync(serverSide.GetStream(), CancellationToken.None);
            return client.GetStream();
        }

        return (server, new RemoteChunkSource(_media, OpenAsync, parallelism: 2));
    }

    /// <summary>
    /// Message channels between participants in memory, delivered in order on the sender's thread.
    /// </summary>
    private sealed class MemoryNetwork
    {
        /// <summary>
        /// The participants.
        /// </summary>
        private readonly ConcurrentDictionary<PeerId, Transport> _members = new();

        /// <summary>
        /// Backing field of <see cref="Messages"/>.
        /// </summary>
        private int _messages;

        /// <summary>
        /// Backing field of <see cref="Refused"/>.
        /// </summary>
        private int _refused;

        /// <summary>
        /// Gets or sets a value indicating whether chunk data is altered on the way.
        /// </summary>
        public bool Corrupt { get; set; }

        /// <summary>
        /// Gets or sets a period after which a send is refused as if the buffer were full (0: never).
        /// </summary>
        public int RefuseEvery { get; set; }

        /// <summary>
        /// Gets the number of delivered messages.
        /// </summary>
        public int Messages => _messages;

        /// <summary>
        /// Gets the number of refused sends.
        /// </summary>
        public int Refused => _refused;

        /// <summary>
        /// Adds a participant.
        /// </summary>
        /// <param name="peer">The participant.</param>
        /// <returns>Its transport.</returns>
        public IPeerMessageTransport Join(PeerId peer) => _members[peer] = new Transport(this, peer);

        /// <summary>
        /// Removes a participant.
        /// </summary>
        /// <param name="peer">The participant.</param>
        public void Leave(PeerId peer) => _members.TryRemove(peer, out _);

        /// <summary>
        /// A transport of one participant.
        /// </summary>
        /// <param name="network">The network.</param>
        /// <param name="self">The participant.</param>
        private sealed class Transport(MemoryNetwork network, PeerId self) : IPeerMessageTransport
        {
            /// <inheritdoc />
            public event EventHandler<PeerMessage>? MessageReceived;

            /// <inheritdoc />
            public IReadOnlyCollection<PeerId> ConnectedPeers
                => network._members.ContainsKey(self) ? network._members.Keys.Where(p => p != self).ToArray() : [];

            /// <inheritdoc />
            public bool TrySend(PeerId peer, ReadOnlySpan<byte> message)
            {
                if (!network._members.ContainsKey(self) || !network._members.TryGetValue(peer, out var target))
                {
                    return false;
                }

                if (network.RefuseEvery > 0 && Interlocked.Increment(ref network._refused) % network.RefuseEvery == 0)
                {
                    return false;
                }

                var copy = message.ToArray();
                if (network.Corrupt && copy[0] == PeerChunkExchange.DataMessage)
                {
                    copy[^1] ^= 0x55;
                }

                Interlocked.Increment(ref network._messages);
                target.MessageReceived?.Invoke(target, new PeerMessage(self, copy));
                return true;
            }
        }
    }
}
