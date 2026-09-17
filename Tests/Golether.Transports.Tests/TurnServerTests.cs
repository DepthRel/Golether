using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Golether.Transports.Relay;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of the TURN relay of the host with a minimal TURN client over loopback.
/// </summary>
public sealed class TurnServerTests : IAsyncLifetime
{
    /// <summary>
    /// The server credentials.
    /// </summary>
    private readonly RelayCredentials _credentials = RelayCredentials.CreateRandom(0);

    /// <summary>
    /// The server.
    /// </summary>
    private TurnServer _server = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        _server = new TurnServer(new TurnServerOptions { Credentials = _credentials, ListenAddress = IPAddress.Loopback });
        _server.Start();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _server.DisposeAsync();

    /// <summary>
    /// Messages survive encoding: types, XOR addresses, integrity and fingerprint.
    /// </summary>
    [Fact]
    public void Codec_RoundTrips()
    {
        var key = Stun.LongTermKey("u", "r", "p");
        var message = new StunMessage(Stun.ChannelBind, Stun.ClassSuccess, RandomNumberGenerator.GetBytes(12))
            .AddXorAddress(Stun.AttrXorPeerAddress, new IPEndPoint(IPAddress.Parse("192.0.2.10"), 5000))
            .AddXorAddress(Stun.AttrXorRelayedAddress, new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6000))
            .AddText(Stun.AttrUsername, "u");
        var bytes = message.Encode(key);

        Assert.Equal(0x0109, BinaryPrimitives.ReadUInt16BigEndian(bytes));
        var parsed = StunMessage.Parse(bytes);
        Assert.Equal(Stun.ChannelBind, parsed.Method);
        Assert.Equal(Stun.ClassSuccess, parsed.Class);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.0.2.10"), 5000), parsed.GetXorAddress(Stun.AttrXorPeerAddress));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6000), parsed.GetXorAddress(Stun.AttrXorRelayedAddress));
        Assert.True(parsed.VerifyIntegrity(key));
        Assert.False(parsed.VerifyIntegrity(Stun.LongTermKey("u", "r", "wrong")));
        Assert.Equal(0x0017, StunMessage.TypeOf(Stun.Data, Stun.ClassIndication));
        Assert.Throws<InvalidDataException>(() => StunMessage.Parse([0x40, 0, 0, 0]));

        // The fingerprint of RFC 5769 test vectors is CRC-32 over the message XOR 0x5354554E; check the CRC itself.
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }

    /// <summary>
    /// Without credentials there is no relay; with them, data flows through Send and Data indications and through a
    /// channel, and only to permitted peers.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Relay_RequiresCredentialsAndPermissions()
    {
        var token = TestContext.Current.CancellationToken;
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var stranger = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var peerAddress = (IPEndPoint)peer.Client.LocalEndPoint!;
        await using var client = await TurnClient.ConnectAsync(_server.Port, token);

        // 1. Unauthenticated allocate gets a challenge; a wrong password is refused.
        var challenge = await client.RequestAsync(new StunMessage(Stun.Allocate, Stun.ClassRequest, RandomNumberGenerator.GetBytes(12))
            .Add(Stun.AttrRequestedTransport, [17, 0, 0, 0]), null, token);
        Assert.Equal(401, challenge.GetErrorCode());
        var realm = challenge.GetText(Stun.AttrRealm)!;
        var nonce = challenge.GetText(Stun.AttrNonce)!;
        var wrong = await client.RequestAsync(Signed(Stun.Allocate, realm, nonce, "not the password").Add(Stun.AttrRequestedTransport, [17, 0, 0, 0]),
            Stun.LongTermKey(_credentials.Username, realm, "not the password"), token);
        Assert.Equal(401, wrong.GetErrorCode());
        Assert.Equal(0, _server.AllocationCount);

        // 2. The right credentials create a relay.
        var key = Stun.LongTermKey(_credentials.Username, realm, _credentials.Password);
        var allocated = await client.RequestAsync(Signed(Stun.Allocate, realm, nonce).Add(Stun.AttrRequestedTransport, [17, 0, 0, 0]), key, token);
        Assert.Equal(Stun.ClassSuccess, allocated.Class);
        Assert.True(allocated.VerifyIntegrity(key));
        var relay = allocated.GetXorAddress(Stun.AttrXorRelayedAddress)!;
        Assert.Equal(IPAddress.Loopback, relay.Address);
        Assert.Equal(1, _server.AllocationCount);

        // 3. Without a permission nothing is relayed in either direction.
        await client.SendRawAsync(Indication(peerAddress, [1, 2, 3]), token);
        await stranger.SendAsync(new byte[] { 9 }, relay, token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReceiveAsync(peer, TimeSpan.FromMilliseconds(300), token));

        var permitted = await client.RequestAsync(Signed(Stun.CreatePermission, realm, nonce).AddXorAddress(Stun.AttrXorPeerAddress, peerAddress), key, token);
        Assert.Equal(Stun.ClassSuccess, permitted.Class);

        // 4. Send indication out, Data indication back.
        await client.SendRawAsync(Indication(peerAddress, [1, 2, 3]), token);
        var outgoing = await ReceiveAsync(peer, TimeSpan.FromSeconds(5), token);
        Assert.Equal([1, 2, 3], outgoing.Buffer);
        Assert.Equal(relay.Port, outgoing.RemoteEndPoint.Port);
        await peer.SendAsync(new byte[] { 4, 5 }, relay, token);
        var data = StunMessage.Parse(await client.ReadFrameAsync(token));
        Assert.Equal((Stun.Data, Stun.ClassIndication), (data.Method, data.Class));
        Assert.Equal([4, 5], data.Get(Stun.AttrData));
        Assert.Equal(peerAddress, data.GetXorAddress(Stun.AttrXorPeerAddress));

        // 5. A channel carries both directions without STUN headers.
        var bound = await client.RequestAsync(Signed(Stun.ChannelBind, realm, nonce)
            .Add(Stun.AttrChannelNumber, [0x40, 0x01, 0, 0])
            .AddXorAddress(Stun.AttrXorPeerAddress, peerAddress), key, token);
        Assert.Equal(Stun.ClassSuccess, bound.Class);
        await client.SendRawAsync([0x40, 0x01, 0, 5, 10, 11, 12, 13, 14, 0, 0, 0], token);
        Assert.Equal([10, 11, 12, 13, 14], (await ReceiveAsync(peer, TimeSpan.FromSeconds(5), token)).Buffer);
        await peer.SendAsync(new byte[] { 7, 7, 7 }, relay, token);
        var channelFrame = await client.ReadFrameAsync(token);
        Assert.Equal([0x40, 0x01, 0, 3, 7, 7, 7], channelFrame);

        // 6. A refresh with lifetime 0 removes the relay.
        var deleted = await client.RequestAsync(Signed(Stun.Refresh, realm, nonce).AddUInt32(Stun.AttrLifetime, 0), key, token);
        Assert.Equal(Stun.ClassSuccess, deleted.Class);
        Assert.Equal(0, _server.AllocationCount);
    }

    /// <summary>
    /// The TURN address carries escaped credentials and TCP transport.
    /// </summary>
    [Fact]
    public void Credentials_FormTurnUri()
    {
        var credentials = new RelayCredentials(47801, "golether-1", "a b:c");

        Assert.Equal("turn://golether-1:a%20b%3Ac@203.0.113.7:47801?transport=tcp", credentials.ToTurnUri("203.0.113.7"));
        Assert.Equal("turn://golether-1:a%20b%3Ac@[2001:db8::1]:47801?transport=tcp", credentials.ToTurnUri("2001:db8::1"));
        Assert.DoesNotContain(['+', '/'], RelayCredentials.CreateRandom(1).Password);
    }

    /// <summary>
    /// Creates a signed request.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="realm">The realm.</param>
    /// <param name="nonce">The nonce.</param>
    /// <param name="username">The user name; the server's when <see langword="null"/>.</param>
    /// <returns>The request.</returns>
    private StunMessage Signed(ushort method, string realm, string nonce, string? username = null)
        => new StunMessage(method, Stun.ClassRequest, RandomNumberGenerator.GetBytes(12))
            .AddText(Stun.AttrUsername, username is null || username.StartsWith("not", StringComparison.Ordinal) ? _credentials.Username : username)
            .AddText(Stun.AttrRealm, realm)
            .AddText(Stun.AttrNonce, nonce);

    /// <summary>
    /// Builds a Send indication.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="data">The payload.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Indication(IPEndPoint peer, byte[] data)
        => new StunMessage(Stun.Send, Stun.ClassIndication, RandomNumberGenerator.GetBytes(12))
            .AddXorAddress(Stun.AttrXorPeerAddress, peer)
            .Add(Stun.AttrData, data)
            .Encode();

    /// <summary>
    /// Receives one UDP packet with a time limit.
    /// </summary>
    /// <param name="udp">The socket.</param>
    /// <param name="timeout">The limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The packet.</returns>
    private static async Task<UdpReceiveResult> ReceiveAsync(UdpClient udp, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        return await udp.ReceiveAsync(limit.Token);
    }

    /// <summary>
    /// A minimal TURN client over TCP.
    /// </summary>
    private sealed class TurnClient : IAsyncDisposable
    {
        /// <summary>
        /// The TCP connection.
        /// </summary>
        private readonly TcpClient _tcp;

        /// <summary>
        /// Initializes a new instance of the <see cref="TurnClient"/> class.
        /// </summary>
        /// <param name="tcp">The connection.</param>
        private TurnClient(TcpClient tcp) => _tcp = tcp;

        /// <summary>
        /// Connects to the server.
        /// </summary>
        /// <param name="port">The port.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The client.</returns>
        public static async Task<TurnClient> ConnectAsync(int port, CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return new TurnClient(tcp);
        }

        /// <summary>
        /// Sends a request and reads the response.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="key">The integrity key, or <see langword="null"/>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response.</returns>
        public async Task<StunMessage> RequestAsync(StunMessage request, byte[]? key, CancellationToken cancellationToken)
        {
            await SendRawAsync(request.Encode(key), cancellationToken);
            return StunMessage.Parse(await ReadFrameAsync(cancellationToken));
        }

        /// <summary>
        /// Sends raw bytes.
        /// </summary>
        /// <param name="data">The bytes.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bytes were sent.</returns>
        public async Task SendRawAsync(byte[] data, CancellationToken cancellationToken)
            => await _tcp.GetStream().WriteAsync(data, cancellationToken);

        /// <summary>
        /// Reads one STUN message or ChannelData frame (without padding).
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The frame.</returns>
        public async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(5));
            var stream = _tcp.GetStream();
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, limit.Token);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
            var isChannel = (header[0] & 0xC0) == 0x40;
            var rest = new byte[isChannel ? (length + 3) & ~3 : Stun.HeaderSize - 4 + length];
            await stream.ReadExactlyAsync(rest, limit.Token);
            return [.. header, .. isChannel ? rest.AsSpan(0, length) : rest];
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
