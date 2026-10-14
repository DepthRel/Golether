using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Golether.Transports.Relay;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of <see cref="RelayMultiplexer"/> and <see cref="SharedRelayRoute"/>: several connections in one stream.
/// </summary>
public sealed class RelayMultiplexerTests
{
    /// <summary>
    /// Two channels of one stream carry their own bytes in both directions and do not mix them up.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Channels_CarryTheirOwnBytes()
    {
        var token = TestContext.Current.CancellationToken;
        var (near, far) = await ConnectedPairAsync(token);
        await using var server = new RelayMultiplexer(far, EchoAsync);
        await using var client = new RelayMultiplexer(near);
        server.Start();
        client.Start();

        await using var first = await client.OpenAsync(token);
        await using var second = await client.OpenAsync(token);

        // More than one frame each, sent at the same time.
        var one = Enumerable.Range(0, 40_000).Select(i => (byte)(i % 251)).ToArray();
        var two = Enumerable.Range(0, 40_000).Select(i => (byte)(i % 13)).ToArray();
        await Task.WhenAll(first.WriteAsync(one, token).AsTask(), second.WriteAsync(two, token).AsTask());

        var back = new byte[one.Length];
        await first.ReadExactlyAsync(back, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(one, back);
        await second.ReadExactlyAsync(back, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(two, back);
    }

    /// <summary>
    /// Closing a channel on one side ends it on the other and leaves the other channels alone.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ClosingAChannel_EndsItOnTheOtherSideOnly()
    {
        var token = TestContext.Current.CancellationToken;
        var (near, far) = await ConnectedPairAsync(token);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var served = 0;
        await using var server = new RelayMultiplexer(far, async channel =>
        {
            var mine = Interlocked.Increment(ref served);
            await EchoAsync(channel);
            if (mine == 1)
            {
                ended.TrySetResult();
            }
        });
        await using var client = new RelayMultiplexer(near);
        server.Start();
        client.Start();
        var first = await client.OpenAsync(token);
        await first.WriteAsync(new byte[] { 1 }, token);
        await first.ReadExactlyAsync(new byte[1], token);
        await using var second = await client.OpenAsync(token);

        await first.DisposeAsync();

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        await second.WriteAsync(new byte[] { 7 }, token);
        var back = new byte[1];
        await second.ReadExactlyAsync(back, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(7, back[0]);
        Assert.True(client.IsAlive);
    }

    /// <summary>
    /// When the shared stream breaks, every channel sees the end and nothing more can be opened.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task BrokenStream_EndsEveryChannel()
    {
        var token = TestContext.Current.CancellationToken;
        var (near, far) = await ConnectedPairAsync(token);
        var server = new RelayMultiplexer(far, EchoAsync);
        await using var client = new RelayMultiplexer(near);
        server.Start();
        client.Start();
        await using var channel = await client.OpenAsync(token);

        await server.DisposeAsync();

        Assert.Equal(0, await channel.ReadAsync(new byte[1], token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
        await client.Completion.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.False(client.IsAlive);
        await Assert.ThrowsAsync<IOException>(() => client.OpenAsync(token));
    }

    /// <summary>
    /// What arrives is not trusted: a frame that claims more bytes than a frame may carry ends the stream, and
    /// channels beyond the limit are refused without being served.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ForeignFrames_AreBounded()
    {
        var token = TestContext.Current.CancellationToken;
        var (near, far) = await ConnectedPairAsync(token);
        var served = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new RelayMultiplexer(far, async _ =>
        {
            Interlocked.Increment(ref served);
            await release.Task;
        });
        server.Start();

        // One more "open" than the limit, written by hand.
        for (uint id = 1; id <= RelayMultiplexer.MaxChannels + 1; id++)
        {
            await near.WriteAsync(Frame(id, 1, 0), token);
        }

        var refusal = new byte[7];
        await near.ReadExactlyAsync(refusal, token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal((uint)RelayMultiplexer.MaxChannels + 1, BinaryPrimitives.ReadUInt32BigEndian(refusal));
        Assert.Equal(3, refusal[4]);
        Assert.Equal(RelayMultiplexer.MaxChannels, Volatile.Read(ref served));

        await near.WriteAsync(Frame(1, 2, RelayMultiplexer.MaxPayload + 1), token);
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.False(server.IsAlive);
        release.SetResult();
    }

    /// <summary>
    /// The route opens the shared stream once for all channels, opens a new one after it broke, and does not repeat
    /// a failed opening for every waiting connection.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Route_SharesOneStreamAndReopensIt()
    {
        var token = TestContext.Current.CancellationToken;
        var servers = new List<RelayMultiplexer>();
        var opened = 0;
        var fail = false;
        await using var route = new SharedRelayRoute(
            async cancellation =>
            {
                Interlocked.Increment(ref opened);
                if (fail)
                {
                    throw new IOException("The host is away.");
                }

                var (near, far) = await ConnectedPairAsync(cancellation);
                var server = new RelayMultiplexer(far, EchoAsync);
                server.Start();
                servers.Add(server);
                return near;
            },
            TimeProvider.System);

        var channels = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => route.OpenAsync(token)));
        Assert.Equal(1, opened);
        foreach (var channel in channels)
        {
            await channel.WriteAsync(new byte[] { 9 }, token);
            await channel.ReadExactlyAsync(new byte[1], token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
        }

        // The stream breaks: the next connection gets a new one.
        await servers[0].DisposeAsync();
        Assert.Equal(0, await channels[0].ReadAsync(new byte[1], token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
        await using var again = await route.OpenAsync(token);
        Assert.Equal(2, opened);

        // It breaks again and cannot be opened: the waiting connections share one failed attempt.
        await servers[1].DisposeAsync();
        Assert.Equal(0, await again.ReadAsync(new byte[1], token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
        fail = true;
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<IOException>(() => route.OpenAsync(token));
        }

        Assert.Equal(3, opened);
        foreach (var server in servers)
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// Sends back what a channel receives until it ends.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <returns>A task that completes when the channel ended.</returns>
    private static async Task EchoAsync(Stream channel)
    {
        var buffer = new byte[8192];
        int read;
        while ((read = await channel.ReadAsync(buffer)) > 0)
        {
            await channel.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    /// <summary>
    /// Builds a frame header that claims a payload of a given length.
    /// </summary>
    /// <param name="channel">The channel number.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="length">The claimed length.</param>
    /// <returns>The header.</returns>
    private static byte[] Frame(uint channel, byte kind, int length)
    {
        var header = new byte[7];
        BinaryPrimitives.WriteUInt32BigEndian(header, channel);
        header[4] = kind;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(5), (ushort)length);
        return header;
    }

    /// <summary>
    /// Makes two ends of a loopback TCP connection.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The two ends.</returns>
    private static async Task<(Stream Near, Stream Far)> ConnectedPairAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
        var accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        accepted.NoDelay = true;
        return (client.GetStream(), accepted.GetStream());
    }
}
