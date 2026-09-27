using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Golether.Core.Data.Enums;
using Golether.Core.Networking;
using Golether.Security.Identity;
using Golether.Transports;
using Golether.Transports.Rendezvous;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Transports.Tls;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Tests;

/// <summary>
/// Whole meetings over loopback: a host holds a room, a participant asks for it, both punch a direct connection.
/// </summary>
public sealed class MeetingTests
{
    /// <summary>
    /// Short client timeouts, so a failing test does not hang.
    /// </summary>
    private static readonly RendezvousClientOptions ClientOptions = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AnswerTimeout = TimeSpan.FromSeconds(10),
        PunchTimeout = TimeSpan.FromSeconds(10),
        RetryDelays = [TimeSpan.FromMilliseconds(200)],
    };

    /// <summary>
    /// TLS options for tests: a free port and a short handshake.
    /// </summary>
    private static readonly TlsTransportOptions TlsOptions = new() { Port = 0, HandshakeTimeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// The participant gets a socket connected straight to the host (not to the server), and the host gets the
    /// other end with the participant's first bytes.
    /// </summary>
    [Fact]
    public async Task Meeting_PunchesADirectConnection()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        var point = server.NewPoint();
        var punched = Channel.CreateUnbounded<Socket>();
        await using var host = new RendezvousHost(point, Owner(identity), socket => punched.Writer.TryWrite(socket), ClientOptions);
        await StartAndWaitAsync(host, token);

        using var outgoing = await new RendezvousDialer(ClientOptions).DialAsync(point, token);
        await outgoing.SendAsync("hello"u8.ToArray(), SocketFlags.None, token);
        using var incoming = await punched.Reader.ReadAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(20), token);

        var received = new byte[5];
        Assert.Equal(5, await incoming.ReceiveAsync(received, SocketFlags.None, token));
        Assert.Equal("hello"u8.ToArray(), received);
        Assert.NotEqual(server.Endpoint.Port, ((IPEndPoint)outgoing.RemoteEndPoint!).Port);
        Assert.Equal(((IPEndPoint)outgoing.LocalEndPoint!).Port, ((IPEndPoint)incoming.RemoteEndPoint!).Port);
        Assert.Equal(1, server.Statistics.Snapshot().MeetingsIntroduced);
    }

    /// <summary>
    /// The session transport runs over the punched connection: the participant pins the key of the host, the
    /// listener of the host authenticates the participant, and the stream carries data both ways.
    /// </summary>
    [Fact]
    public async Task Meeting_CarriesMutualTls()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(hostIdentity, TlsOptions);
        listener.Start();
        var point = server.NewPoint();
        await using var host = new RendezvousHost(point, Owner(hostIdentity), socket => listener.Adopt(socket), ClientOptions);
        await StartAndWaitAsync(host, token);
        var connector = new RendezvousPeerConnector(new TlsPeerConnector(guestIdentity, TlsOptions), new RendezvousDialer(ClientOptions));

        var accepting = listener.AcceptAsync(token).AsTask();
        await using var outgoing = await connector.ConnectAsync(point, hostIdentity.PeerId, StreamPurpose.Control, token);
        await using var incoming = await accepting.WaitAsync(TimeSpan.FromSeconds(20), token);

        Assert.Equal(hostIdentity.PeerId, outgoing.RemotePeer);
        Assert.Equal(guestIdentity.PeerId, incoming.RemotePeer);
        Assert.Equal(StreamPurpose.Control, incoming.Purpose);
        await outgoing.Stream.WriteAsync("ping"u8.ToArray(), token);
        await outgoing.Stream.FlushAsync(token);
        var buffer = new byte[4];
        await incoming.Stream.ReadExactlyAsync(buffer, token);
        Assert.Equal("ping"u8.ToArray(), buffer);
    }

    /// <summary>
    /// A participant that expects another key refuses the host: the server cannot substitute a device.
    /// </summary>
    [Fact]
    public async Task Meeting_RefusesAHostWithAnotherKey()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var expectedIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(hostIdentity, TlsOptions);
        listener.Start();
        var point = server.NewPoint();
        await using var host = new RendezvousHost(point, Owner(hostIdentity), socket => listener.Adopt(socket), ClientOptions);
        await StartAndWaitAsync(host, token);
        var connector = new RendezvousPeerConnector(new TlsPeerConnector(guestIdentity, TlsOptions), new RendezvousDialer(ClientOptions));

        await Assert.ThrowsAsync<PeerAuthenticationException>(() => connector.ConnectAsync(point, expectedIdentity.PeerId, StreamPurpose.Control, token));
    }

    /// <summary>
    /// Several streams of one participant meet the host one after another, as a session opens its control and media
    /// streams.
    /// </summary>
    [Fact]
    public async Task Meeting_ServesSeveralStreams()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(hostIdentity, TlsOptions);
        listener.Start();
        var point = server.NewPoint();
        await using var host = new RendezvousHost(point, Owner(hostIdentity), socket => listener.Adopt(socket), ClientOptions);
        await StartAndWaitAsync(host, token);
        var connector = new RendezvousPeerConnector(new TlsPeerConnector(guestIdentity, TlsOptions), new RendezvousDialer(ClientOptions));

        var streams = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => connector.ConnectAsync(point, hostIdentity.PeerId, StreamPurpose.MediaData, token)));
        for (var i = 0; i < streams.Length; i++)
        {
            await using var accepted = await listener.AcceptAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.Equal(guestIdentity.PeerId, accepted.RemotePeer);
        }

        foreach (var stream in streams)
        {
            await stream.DisposeAsync();
        }

        Assert.Equal(3, server.Statistics.Snapshot().MeetingsIntroduced);
    }

    /// <summary>
    /// Asking for a room nobody holds ends with <see cref="RendezvousError.RoomNotFound"/>.
    /// </summary>
    [Fact]
    public async Task Connect_ToAnEmptyRoom_ReportsRoomNotFound()
    {
        await using var server = await TestServer.StartAsync();

        var error = await Assert.ThrowsAsync<RendezvousException>(() => new RendezvousDialer(ClientOptions).DialAsync(server.NewPoint(), TestContext.Current.CancellationToken));

        Assert.Equal(RendezvousError.RoomNotFound, error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    /// <summary>
    /// A server that is not there is reported as <see cref="RendezvousError.Unreachable"/>.
    /// </summary>
    [Fact]
    public async Task Connect_ToNoServer_ReportsUnreachable()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var point = new RendezvousPoint(new PeerEndpoint("127.0.0.1", port), RendezvousPoint.CreateRoom());

        var error = await Assert.ThrowsAsync<RendezvousException>(() => new RendezvousDialer(ClientOptions).DialAsync(point, TestContext.Current.CancellationToken));

        Assert.Equal(RendezvousError.Unreachable, error.Error);
    }

    /// <summary>
    /// A host whose signature does not match its certificate does not get the room.
    /// </summary>
    [Fact]
    public async Task Register_WithABadSignature_IsRefused()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var other = DeviceIdentity.CreateNew(TimeProvider.System);
        var forged = new RendezvousOwner(identity.PublicCertificateDer, data => other.Sign(data));
        await using var host = new RendezvousHost(server.NewPoint(), forged, socket => socket.Dispose(), ClientOptions);

        var error = await WaitForErrorAsync(host, token);

        Assert.Equal(RendezvousError.BadSignature, error);
        Assert.Equal(0, server.Rooms.Count);
    }

    /// <summary>
    /// A room held by one device cannot be taken by another one.
    /// </summary>
    [Fact]
    public async Task Register_ARoomOfAnotherDevice_IsRefused()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var first = DeviceIdentity.CreateNew(TimeProvider.System);
        using var second = DeviceIdentity.CreateNew(TimeProvider.System);
        var point = server.NewPoint();
        await using var owner = new RendezvousHost(point, Owner(first), socket => socket.Dispose(), ClientOptions);
        await StartAndWaitAsync(owner, token);
        await using var intruder = new RendezvousHost(point, Owner(second), socket => socket.Dispose(), ClientOptions);

        var error = await WaitForErrorAsync(intruder, token);

        Assert.Equal(RendezvousError.RoomTaken, error);
        Assert.True(owner.IsRegistered);
    }

    /// <summary>
    /// A host that does not answer leaves the participant with <see cref="RendezvousError.Timeout"/>, and the
    /// meetings over the limit of the room are refused as <see cref="RendezvousError.Busy"/>.
    /// </summary>
    [Fact]
    public async Task Connect_ToASilentHost_TimesOutAndLimitsWaiting()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync(new RendezvousServerOptions { AnswerTimeout = TimeSpan.FromSeconds(2), MaxPendingPerRoom = 1 });
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        var point = server.NewPoint();
        await using var silent = await SilentHost.RegisterAsync(point, identity, token);
        var dialer = new RendezvousDialer(ClientOptions);

        var waiting = dialer.DialAsync(point, token);
        await Task.Delay(300, token);
        var refused = await Assert.ThrowsAsync<RendezvousException>(() => dialer.DialAsync(point, token));
        var timedOut = await Assert.ThrowsAsync<RendezvousException>(() => waiting);

        Assert.Equal(RendezvousError.Busy, refused.Error);
        Assert.Equal(RendezvousError.Timeout, timedOut.Error);
    }

    /// <summary>
    /// When the host leaves, its room is released and participants learn that it is gone.
    /// </summary>
    [Fact]
    public async Task HostLeaving_ReleasesTheRoom()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await TestServer.StartAsync();
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        var point = server.NewPoint();
        var host = new RendezvousHost(point, Owner(identity), socket => socket.Dispose(), ClientOptions);
        await StartAndWaitAsync(host, token);
        Assert.Equal(1, server.Rooms.Count);

        await host.DisposeAsync();
        for (var i = 0; i < 50 && server.Rooms.Count > 0; i++)
        {
            await Task.Delay(100, token);
        }

        Assert.Equal(0, server.Rooms.Count);
        var error = await Assert.ThrowsAsync<RendezvousException>(() => new RendezvousDialer(ClientOptions).DialAsync(point, token));
        Assert.Equal(RendezvousError.RoomNotFound, error.Error);
    }

    /// <summary>
    /// Builds the owner of a room from a device identity.
    /// </summary>
    /// <param name="identity">The identity.</param>
    /// <returns>The owner.</returns>
    private static RendezvousOwner Owner(DeviceIdentity identity) => new(identity.PublicCertificateDer, data => identity.Sign(data));

    /// <summary>
    /// Starts a host and waits until it holds its room.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the room is held.</returns>
    private static async Task StartAndWaitAsync(RendezvousHost host, CancellationToken cancellationToken)
    {
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StateChanged += (_, _) =>
        {
            if (host.IsRegistered)
            {
                registered.TrySetResult();
            }
        };
        host.Start();
        await registered.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    /// <summary>
    /// Starts a host and waits for its first failure.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reason of the failure.</returns>
    private static async Task<RendezvousError> WaitForErrorAsync(RendezvousHost host, CancellationToken cancellationToken)
    {
        var failed = new TaskCompletionSource<RendezvousError>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StateChanged += (_, _) =>
        {
            if (host.LastError is { } error)
            {
                failed.TrySetResult(error);
            }
        };
        host.Start();
        return await failed.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    /// <summary>
    /// A host that holds a room but never answers a meeting.
    /// </summary>
    private sealed class SilentHost : IAsyncDisposable
    {
        /// <summary>
        /// The registration connection.
        /// </summary>
        private readonly RendezvousChannel _channel;

        /// <summary>
        /// Initializes a new instance of the <see cref="SilentHost"/> class.
        /// </summary>
        /// <param name="channel">The registration connection.</param>
        private SilentHost(RendezvousChannel channel) => _channel = channel;

        /// <summary>
        /// Registers a room with the protocol messages directly.
        /// </summary>
        /// <param name="point">The server and the room.</param>
        /// <param name="identity">The device key.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The registered host.</returns>
        public static async Task<SilentHost> RegisterAsync(RendezvousPoint point, DeviceIdentity identity, CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            await client.ConnectAsync(point.Server.Host, point.Server.Port, cancellationToken);
            var channel = new RendezvousChannel(client.GetStream());
            var hello = Assert.IsType<ServerHelloMessage>(await channel.ReceiveAsync(cancellationToken));
            var signature = identity.Sign(RendezvousSignature.BuildPayload(hello.Nonce, point.Room));
            await channel.SendAsync(new RegisterMessage(point.Room, Convert.ToBase64String(identity.PublicCertificateDer), Convert.ToBase64String(signature)), cancellationToken);
            Assert.IsType<RegisteredMessage>(await channel.ReceiveAsync(cancellationToken));
            return new SilentHost(channel);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => _channel.DisposeAsync();
    }
}
