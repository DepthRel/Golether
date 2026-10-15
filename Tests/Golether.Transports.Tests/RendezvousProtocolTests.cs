using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Golether.Core.Enums;
using Golether.Core.Networking;
using Golether.Localization;
using Golether.Security.Identity;
using Golether.Transports.Rendezvous;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Transports.Rendezvous.Punching;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of the rendezvous protocol shared by the server and the devices, and of TCP hole punching.
/// </summary>
public sealed class RendezvousProtocolTests
{
    /// <summary>
    /// Every message survives the channel with its type and fields.
    /// </summary>
    [Fact]
    public async Task Channel_RoundTripsEveryMessage()
    {
        RendezvousMessage[] messages =
        [
            new ServerHelloMessage(1, RendezvousProtocol.CreateRandomId()),
            new RegisterMessage(RendezvousPoint.CreateRoom(), "Y2VydA==", "c2ln"),
            new RegisteredMessage("203.0.113.7:50000"),
            new ConnectMessage(RendezvousPoint.CreateRoom(), ["192.168.1.5:50001"]),
            new IncomingMessage(RendezvousProtocol.CreateRandomId()),
            new AcceptMessage(RendezvousProtocol.CreateRandomId(), []),
            new PunchMessage("198.51.100.9:40000", ["10.0.0.2:40000"]),
            new ErrorMessage(RendezvousError.RoomTaken),
            new PingMessage(),
            new PongMessage(),
        ];
        var buffer = new MemoryStream();
        var writer = new RendezvousChannel(buffer);
        foreach (var message in messages)
        {
            await writer.SendAsync(message, TestContext.Current.CancellationToken);
        }

        buffer.Position = 0;
        var reader = new RendezvousChannel(buffer);
        foreach (var message in messages)
        {
            var received = await reader.ReceiveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(message.GetType(), received!.GetType());
            Assert.Equal(RendezvousChannel.Serialize(message), RendezvousChannel.Serialize(received));
        }

        Assert.Null(await reader.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Errors travel as names, not numbers, so a reader words them in its own language.
    /// </summary>
    [Fact]
    public void Errors_AreSentAsNames()
    {
        var json = System.Text.Encoding.UTF8.GetString(RendezvousChannel.Serialize(new ErrorMessage(RendezvousError.RoomNotFound)));

        Assert.Contains("\"RoomNotFound\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unknown types, garbage and oversized or empty frames are refused as invalid data.
    /// </summary>
    [Fact]
    public async Task Channel_RefusesInvalidFrames()
    {
        Assert.Throws<InvalidDataException>(() => RendezvousChannel.Deserialize("{\"$t\":\"launch\"}"u8));
        Assert.Throws<InvalidDataException>(() => RendezvousChannel.Deserialize("not json"u8));

        foreach (var length in new[] { 0, RendezvousProtocol.MAX_FRAME_SIZE + 1, -1 })
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, length);
            var channel = new RendezvousChannel(new MemoryStream(header));
            await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReceiveAsync(TestContext.Current.CancellationToken).AsTask());
        }

        var truncated = new byte[] { 0, 0, 0, 10, (byte)'{' };
        await Assert.ThrowsAsync<InvalidDataException>(() => new RendezvousChannel(new MemoryStream(truncated)).ReceiveAsync(TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>
    /// A signature of the device key proves the room; another nonce, another room or another key do not.
    /// </summary>
    [Fact]
    public void Signature_BindsKeyNonceAndRoom()
    {
        using var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var other = DeviceIdentity.CreateNew(TimeProvider.System);
        var nonce = RendezvousProtocol.CreateRandomId();
        var room = RendezvousPoint.CreateRoom();
        var payload = RendezvousSignature.BuildPayload(nonce, room);
        var signature = identity.Sign(payload);

        Assert.True(RendezvousSignature.TryVerify(identity.PublicCertificateDer, payload, signature, out var owner));
        Assert.Equal(identity.PeerId, owner);
        Assert.False(RendezvousSignature.TryVerify(identity.PublicCertificateDer, RendezvousSignature.BuildPayload(RendezvousProtocol.CreateRandomId(), room), signature, out _));
        Assert.False(RendezvousSignature.TryVerify(identity.PublicCertificateDer, RendezvousSignature.BuildPayload(nonce, RendezvousPoint.CreateRoom()), signature, out _));
        Assert.False(RendezvousSignature.TryVerify(other.PublicCertificateDer, payload, signature, out _));
        Assert.False(RendezvousSignature.TryVerify([1, 2, 3], payload, signature, out _));
        Assert.False(RendezvousSignature.TryVerify(identity.PublicCertificateDer, payload, new byte[RendezvousSignature.MAX_SIGNATURE_SIZE + 1], out _));
    }

    /// <summary>
    /// Base64 fields are decoded only within their limits.
    /// </summary>
    [Fact]
    public void Decode_EnforcesLimits()
    {
        Assert.True(RendezvousSignature.TryDecode(Convert.ToBase64String(new byte[100]), 100, out var bytes));
        Assert.Equal(100, bytes.Length);
        Assert.False(RendezvousSignature.TryDecode(Convert.ToBase64String(new byte[101]), 100, out _));
        Assert.False(RendezvousSignature.TryDecode("%%%", 100, out _));
        Assert.False(RendezvousSignature.TryDecode(string.Empty, 100, out _));
        Assert.False(RendezvousSignature.TryDecode(null, 100, out _));
    }

    /// <summary>
    /// Only IPv4 addresses with a port are accepted from the other side; names, IPv6 and the unspecified address are
    /// not, and the list of local addresses is capped.
    /// </summary>
    [Fact]
    public void Endpoints_AcceptOnlyUsableIPv4()
    {
        Assert.True(RendezvousProtocol.TryParseEndpoint("203.0.113.7:47900", out var endpoint));
        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.7"), 47900), endpoint);
        foreach (var bad in new[] { "example.org:1", "[2001:db8::1]:5", "0.0.0.0:5", "255.255.255.255:5", "1.2.3.4", "1.2.3.4:0", "1.2.3.4:70000", string.Empty })
        {
            Assert.False(RendezvousProtocol.TryParseEndpoint(bad, out _), bad);
        }

        var many = Enumerable.Range(1, 20).Select(i => $"10.0.0.{i}:1000").Append("junk").ToArray();
        Assert.Equal(RendezvousProtocol.MAX_LOCALS, RendezvousProtocol.ParseLocals(many).Count);
        Assert.Empty(RendezvousProtocol.ParseLocals(null));
        Assert.Equal("1.2.3.4:5", RendezvousProtocol.Format(new IPEndPoint(IPAddress.Parse("1.2.3.4").MapToIPv6(), 5)));
    }

    /// <summary>
    /// Rooms and identifiers are random and keep their format.
    /// </summary>
    [Fact]
    public void Ids_AreRandomAndValid()
    {
        var room = RendezvousPoint.CreateRoom();
        Assert.True(RendezvousPoint.IsValidRoom(room));
        Assert.NotEqual(room, RendezvousPoint.CreateRoom());
        Assert.True(RendezvousProtocol.IsValidRandomId(RendezvousProtocol.CreateRandomId()));
        Assert.False(RendezvousPoint.IsValidRoom("short"));
        Assert.False(RendezvousPoint.IsValidRoom(new string('!', RendezvousPoint.ROOM_LENGTH)));
        Assert.Equal("stun://203.0.113.7:47900", new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), room).StunUri);
    }

    /// <summary>
    /// Two sockets on fixed ports connect to each other at the same time and meet in one connection, the way two
    /// devices behind routers do.
    /// </summary>
    [Fact]
    public async Task Punch_ConnectsTwoSidesThatBothDial()
    {
        var token = TestContext.Current.CancellationToken;
        using var holdA = TcpPunch.Bind(0);
        using var holdB = TcpPunch.Bind(0);
        var portA = ((IPEndPoint)holdA.LocalEndPoint!).Port;
        var portB = ((IPEndPoint)holdB.LocalEndPoint!).Port;

        var answering = TcpPunch.AnswerAsync(portB, [new IPEndPoint(IPAddress.Loopback, portA)], TimeSpan.FromSeconds(10), token);
        using var dialed = await TcpPunch.ConnectAsync(portA, [new IPEndPoint(IPAddress.Loopback, portB)], TimeSpan.FromSeconds(10), token);
        await dialed.SendAsync("hi"u8.ToArray(), SocketFlags.None, token);
        using var answered = await answering.WaitAsync(TimeSpan.FromSeconds(15), token);

        var buffer = new byte[2];
        Assert.Equal(2, await answered.ReceiveAsync(buffer, SocketFlags.None, token));
        Assert.Equal("hi"u8.ToArray(), buffer);
        Assert.Equal(portB, ((IPEndPoint)answered.LocalEndPoint!).Port);
        Assert.Equal(portA, ((IPEndPoint)answered.RemoteEndPoint!).Port);
    }

    /// <summary>
    /// When nobody answers, punching gives up in time with <see cref="RendezvousError.PunchFailed"/>.
    /// </summary>
    [Fact]
    public async Task Punch_GivesUpWhenNobodyAnswers()
    {
        using var hold = TcpPunch.Bind(0);
        using var silent = TcpPunch.Bind(0);
        var port = ((IPEndPoint)hold.LocalEndPoint!).Port;
        var target = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)silent.LocalEndPoint!).Port);

        var error = await Assert.ThrowsAsync<RendezvousException>(() => TcpPunch.ConnectAsync(port, [target], TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Equal(RendezvousError.PunchFailed, error.Error);
    }

    /// <summary>
    /// Every reason has a text in every language: the device words what the server only names.
    /// </summary>
    [Fact]
    public void EveryError_HasATextInEveryLanguage()
    {
        var catalog = LanguageCatalog.LoadEmbedded();
        foreach (var language in catalog.Languages)
        {
            var pack = catalog.Find(language.Code)!;
            foreach (var error in Enum.GetValues<RendezvousError>())
            {
                Assert.True(pack.TryGet("Rendezvous.Error." + error, out var text) && !string.IsNullOrWhiteSpace(text), $"{language.Code}: {error}");
            }
        }
    }
}
