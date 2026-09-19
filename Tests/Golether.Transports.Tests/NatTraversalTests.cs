using System.Net;
using System.Net.Sockets;
using Golether.Transports.Relay;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of the public address discovery and the hole punching, against a STUN server running in the test.
/// </summary>
public sealed class NatTraversalTests
{
    /// <summary>
    /// The address reported by a server is returned together with the local port; two servers agreeing means the
    /// router keeps one mapping per port and the address is worth putting into an invitation.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Discover_ReturnsTheAddressTwoServersAgreeOn()
    {
        var token = TestContext.Current.CancellationToken;
        var seen = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 41234);
        await using var first = new FakeStunServer(seen);
        await using var second = new FakeStunServer(seen);
        using var socket = NatTraversal.Open(0);

        var found = await NatTraversal.DiscoverAsync(socket, [first.Address, second.Address], token);

        Assert.NotNull(found);
        Assert.Equal(seen, found.Endpoint);
        Assert.Equal(((IPEndPoint)socket.LocalEndPoint!).Port, found.LocalPort);
        Assert.True(found.IsPredictable);
    }

    /// <summary>
    /// A router that gives every destination its own port is recognised: the address must not be trusted then.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Discover_MarksASymmetricRouter()
    {
        var token = TestContext.Current.CancellationToken;
        await using var first = new FakeStunServer(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 41234));
        await using var second = new FakeStunServer(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 51999));
        using var socket = NatTraversal.Open(0);

        var found = await NatTraversal.DiscoverAsync(socket, [first.Address, second.Address], token);

        Assert.NotNull(found);
        Assert.False(found.IsPredictable);
    }

    /// <summary>
    /// A server that says nothing is skipped, and when nobody answers the caller learns nothing instead of waiting
    /// forever.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Discover_SurvivesSilentAndBrokenServers()
    {
        var token = TestContext.Current.CancellationToken;
        await using var silent = new FakeStunServer(null);
        await using var good = new FakeStunServer(new IPEndPoint(IPAddress.Parse("198.51.100.4"), 3000));
        using var socket = NatTraversal.Open(0);

        var found = await NatTraversal.DiscoverAsync(socket, [silent.Address, good.Address], token);

        Assert.NotNull(found);
        Assert.Equal(3000, found.Endpoint.Port);

        // Nothing usable: no address, and no exception.
        await using var alsoSilent = new FakeStunServer(null);
        using var another = NatTraversal.Open(0);
        Assert.Null(await NatTraversal.DiscoverAsync(another, [silent.Address, alsoSilent.Address], token));
        Assert.Null(await NatTraversal.DiscoverAsync(another, ["not-an-address", "host-without-port"], token));
    }

    /// <summary>
    /// The punch sends packets from the port the tunnel will use, so the router of the sender opens the path; an
    /// unreachable address does not stop the rest.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Punch_SendsFromTheTunnelPort()
    {
        var token = TestContext.Current.CancellationToken;
        using var peer = NatTraversal.Open(0);
        var peerEndpoint = (IPEndPoint)peer.LocalEndPoint!;
        using var socket = NatTraversal.Open(0);
        var sourcePort = ((IPEndPoint)socket.LocalEndPoint!).Port;

        await NatTraversal.PunchAsync(
            socket,
            [new IPEndPoint(IPAddress.Loopback, peerEndpoint.Port), new IPEndPoint(IPAddress.Parse("192.0.2.1"), 47800)],
            rounds: 2,
            cancellationToken: token);

        var buffer = new byte[64];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var received = await peer.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);

        Assert.Equal(sourcePort, ((IPEndPoint)received.RemoteEndPoint).Port);
    }

    /// <summary>
    /// The discovery must not be a coin toss: fifty rounds against two servers all find the address.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Discover_IsRepeatable()
    {
        var token = TestContext.Current.CancellationToken;
        var seen = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 41234);
        await using var first = new FakeStunServer(seen);
        await using var second = new FakeStunServer(seen);
        var misses = 0;
        for (var i = 0; i < 50; i++)
        {
            using var socket = NatTraversal.Open(0);
            var found = await NatTraversal.DiscoverAsync(socket, [first.Address, second.Address], token);
            if (found is not { IsPredictable: true })
            {
                misses++;
            }
        }

        Assert.Equal(0, misses);
    }

    /// <summary>
    /// A STUN server that answers with a fixed address, or says nothing at all.
    /// </summary>
    private sealed class FakeStunServer : IAsyncDisposable
    {
        /// <summary>
        /// The socket.
        /// </summary>
        private readonly Socket _socket = NatTraversal.Open(0);

        /// <summary>
        /// Stops the loop.
        /// </summary>
        private readonly CancellationTokenSource _stop = new();

        /// <summary>
        /// The loop.
        /// </summary>
        private readonly Task _loop;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeStunServer"/> class.
        /// </summary>
        /// <param name="reply">The address to report, or <see langword="null"/> to stay silent.</param>
        public FakeStunServer(IPEndPoint? reply)
        {
            Address = "127.0.0.1:" + ((IPEndPoint)_socket.LocalEndPoint!).Port;
            _loop = RunAsync(reply, _stop.Token);
        }

        /// <summary>
        /// Gets the address of the server as <c>host:port</c>.
        /// </summary>
        public string Address { get; }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }

            _socket.Dispose();
            _stop.Dispose();
        }

        /// <summary>
        /// Answers binding requests.
        /// </summary>
        /// <param name="reply">The address to report, or <see langword="null"/>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the server stops.</returns>
        private async Task RunAsync(IPEndPoint? reply, CancellationToken cancellationToken)
        {
            var buffer = new byte[StunMessage.MaxSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketReceiveFromResult received;
                try
                {
                    received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), cancellationToken);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return;
                }

                if (reply is null || received.ReceivedBytes < Stun.HeaderSize)
                {
                    continue;
                }

                var request = StunMessage.Parse(buffer.AsSpan(0, received.ReceivedBytes));
                var response = new StunMessage(Stun.Binding, Stun.ClassSuccess, request.TransactionId);
                response.AddXorAddress(Stun.AttrXorMappedAddress, reply);
                await _socket.SendToAsync(response.Encode(), SocketFlags.None, received.RemoteEndPoint, cancellationToken);
            }
        }
    }
}
