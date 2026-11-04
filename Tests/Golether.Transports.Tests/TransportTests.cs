using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Golether.Core.Data.Enums;
using Golether.Core.Networking;
using Golether.Security.Identity;
using Golether.Transports.Tls;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of <see cref="FrameChannel"/> and the TLS transport over loopback.
/// </summary>
public sealed class TransportTests
{
    /// <summary>
    /// Transport options for tests: a free port and short timeouts.
    /// </summary>
    private static readonly TlsTransportOptions _options = new() { Port = 0, HandshakeTimeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Frames of different sizes arrive intact and the end of the stream is reported as <see langword="null"/>.
    /// </summary>
    [Fact]
    public async Task FrameChannel_RoundTripsFrames()
    {
        var buffer = new MemoryStream();
        var writer = new FrameChannel(new NonClosingStream(buffer));
        await writer.SendAsync("abc"u8.ToArray(), TestContext.Current.CancellationToken);
        await writer.SendAsync(Array.Empty<byte>(), TestContext.Current.CancellationToken);
        await writer.SendAsync(new byte[70_000], TestContext.Current.CancellationToken);

        buffer.Position = 0;
        var reader = new FrameChannel(buffer);

        Assert.Equal("abc"u8.ToArray(), await reader.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Empty((await reader.ReceiveAsync(TestContext.Current.CancellationToken))!);
        Assert.Equal(70_000, (await reader.ReceiveAsync(TestContext.Current.CancellationToken))!.Length);
        Assert.Null(await reader.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Oversized frames are refused on both sides and truncated frames are detected.
    /// </summary>
    [Fact]
    public async Task FrameChannel_EnforcesLimits()
    {
        var channel = new FrameChannel(new MemoryStream(), maxFrameSize: 10);
        await Assert.ThrowsAsync<ArgumentException>(() => channel.SendAsync(new byte[11], TestContext.Current.CancellationToken).AsTask());

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 11);
        var oversized = new FrameChannel(new MemoryStream(header), maxFrameSize: 10);
        await Assert.ThrowsAsync<InvalidDataException>(() => oversized.ReceiveAsync(TestContext.Current.CancellationToken).AsTask());

        BinaryPrimitives.WriteInt32BigEndian(header, 5);
        var truncated = new FrameChannel(new MemoryStream([.. header, 1, 2]), maxFrameSize: 10);
        await Assert.ThrowsAsync<InvalidDataException>(() => truncated.ReceiveAsync(TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>
    /// A connector that pins the right key connects; both sides know the authenticated identifiers.
    /// </summary>
    [Fact]
    public async Task Tls_ConnectsWithPinnedKey()
    {
        using var server = DeviceIdentity.CreateNew(TimeProvider.System);
        using var client = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(server, _options);
        listener.Start();
        var connector = new TlsPeerConnector(client, _options);
        var token = TestContext.Current.CancellationToken;

        var accepting = listener.AcceptAsync(token).AsTask();
        await using var outgoing = await connector.ConnectAsync(new PeerEndpoint("127.0.0.1", listener.Port), server.PeerId, StreamPurpose.MediaData, token);
        await using var incoming = await accepting.WaitAsync(TimeSpan.FromSeconds(15), token);

        Assert.Equal(server.PeerId, outgoing.RemotePeer);
        Assert.Equal(client.PeerId, incoming.RemotePeer);
        Assert.Equal(StreamPurpose.MediaData, incoming.Purpose);

        await outgoing.Stream.WriteAsync("ping"u8.ToArray(), token);
        await outgoing.Stream.FlushAsync(token);
        var received = new byte[4];
        await incoming.Stream.ReadExactlyAsync(received, token);
        Assert.Equal("ping"u8.ToArray(), received);
    }

    /// <summary>
    /// A relay stream is accepted like the others, and the listener tells at which addresses the connection ends:
    /// the relay of the host needs them.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tls_AcceptsRelayStreamWithItsAddresses()
    {
        using var server = DeviceIdentity.CreateNew(TimeProvider.System);
        using var client = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(server, _options);
        listener.Start();
        var connector = new TlsPeerConnector(client, _options);
        var token = TestContext.Current.CancellationToken;

        var accepting = listener.AcceptAsync(token).AsTask();
        await using var outgoing = await connector.ConnectAsync(new PeerEndpoint("127.0.0.1", listener.Port), server.PeerId, StreamPurpose.Relay, token);
        await using var incoming = await accepting.WaitAsync(TimeSpan.FromSeconds(15), token);

        Assert.Equal(StreamPurpose.Relay, incoming.Purpose);
        Assert.Equal(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, listener.Port), incoming.LocalEndPoint);
        Assert.Equal(System.Net.IPAddress.Loopback, incoming.RemoteEndPoint!.Address);
    }

    /// <summary>
    /// A connector that expects another key refuses the connection.
    /// </summary>
    [Fact]
    public async Task Tls_RejectsWrongKey()
    {
        using var server = DeviceIdentity.CreateNew(TimeProvider.System);
        using var impostorExpected = DeviceIdentity.CreateNew(TimeProvider.System);
        using var client = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(server, _options);
        listener.Start();
        var connector = new TlsPeerConnector(client, _options);

        var error = await Assert.ThrowsAsync<PeerAuthenticationException>(() => connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", listener.Port), impostorExpected.PeerId, StreamPurpose.Control, TestContext.Current.CancellationToken));
        Assert.Contains(server.PeerId.ToShortString(), error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A closed port is reported as an I/O error.
    /// </summary>
    [Fact]
    public async Task Tls_ReportsUnreachableHost()
    {
        using var client = DeviceIdentity.CreateNew(TimeProvider.System);
        using var server = DeviceIdentity.CreateNew(TimeProvider.System);
        int port;
        await using (var probe = new TlsPeerListener(server, _options))
        {
            probe.Start();
            port = probe.Port;
        }

        var connector = new TlsPeerConnector(client, _options);
        await Assert.ThrowsAsync<IOException>(() => connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", port), server.PeerId, StreamPurpose.Control, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Silent connections from one address cannot take every handshake of the listener: beyond the share of an
    /// address its next connections are dropped at once, and the share comes back when they end.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Tls_LimitsTheHandshakesOfOneAddress()
    {
        // Loopback is not counted, so the listener is reached at an address of the network of this device.
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        Assert.SkipWhen(address is null, "This device has no network address besides loopback.");
        var token = TestContext.Current.CancellationToken;
        using var server = DeviceIdentity.CreateNew(TimeProvider.System);
        using var client = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(server, _options with { MaxPendingHandshakesPerAddress = 1 });
        listener.Start();
        var endpoint = new PeerEndpoint(address.ToString(), listener.Port);

        // Two silent connections: the listener keeps the one it counted first and drops the other.
        using var first = new TcpClient();
        using var second = new TcpClient();
        await first.ConnectAsync(address, listener.Port, token);
        await second.ConnectAsync(address, listener.Port, token);
        var firstClosed = WaitClosedAsync(first, token);
        var secondClosed = WaitClosedAsync(second, token);
        var dropped = await Task.WhenAny(firstClosed, secondClosed).WaitAsync(TimeSpan.FromSeconds(5), token);
        using var silent = dropped == firstClosed ? second : first;

        var connector = new TlsPeerConnector(client, _options);
        await Assert.ThrowsAnyAsync<IOException>(() => connector.ConnectAsync(endpoint, server.PeerId, StreamPurpose.Control, token));

        silent.Dispose();
        var accepting = listener.AcceptAsync(token).AsTask();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                await using var outgoing = await connector.ConnectAsync(endpoint, server.PeerId, StreamPurpose.Control, token);
                await using var incoming = await accepting.WaitAsync(TimeSpan.FromSeconds(10), token);
                Assert.Equal(client.PeerId, incoming.RemotePeer);
                break;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                // The listener has not noticed the end of the silent connection yet.
                await Task.Delay(100, token);
            }
        }
    }

    /// <summary>
    /// Waits until the other side closes a connection.
    /// </summary>
    /// <param name="client">The connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the connection was closed.</returns>
    private static async Task WaitClosedAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[64];
        try
        {
            while (await client.GetStream().ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // A reset closes it as well, and so does this side closing it.
        }
    }

    /// <summary>
    /// A stream that ignores disposal so a test can read back what was written.
    /// </summary>
    /// <param name="inner">The wrapped stream.</param>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => inner.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => inner.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => inner.CanWrite;

        /// <inheritdoc />
        public override long Length => inner.Length;

        /// <inheritdoc />
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        /// <inheritdoc />
        public override void Flush() => inner.Flush();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        /// <inheritdoc />
        public override void SetLength(long value) => inner.SetLength(value);

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
