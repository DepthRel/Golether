using System.Net;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Transports.Relay;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;
using Golether.Web.Server.Rendezvous;
using Golether.Web.Server.Stun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Golether.Web.Server.Tests;

/// <summary>
/// Tests of the parts of the server: settings, limits, rooms and STUN.
/// </summary>
public sealed class ServerPartsTests
{
    /// <summary>
    /// Every line of the console log starts with the date, the time and the offset from UTC, unless the
    /// administrator set a format of their own.
    /// </summary>
    [Fact]
    public void Log_StampsLinesWithDateAndTime()
    {
        var moment = new DateTimeOffset(2026, 10, 11, 16, 14, 12, 250, TimeSpan.FromHours(7));

        using var defaults = new ServiceCollection().AddLogTimestamps().BuildServiceProvider();
        var format = defaults.GetRequiredService<IOptions<SimpleConsoleFormatterOptions>>().Value.TimestampFormat;
        Assert.Equal("2026-10-11 16:14:12.250 +07:00 ", moment.ToString(format, System.Globalization.CultureInfo.InvariantCulture));

        var configured = new ServiceCollection();
        configured.Configure<SimpleConsoleFormatterOptions>(options => options.TimestampFormat = "HH:mm ");
        using var custom = configured.AddLogTimestamps().BuildServiceProvider();
        Assert.Equal("HH:mm ", custom.GetRequiredService<IOptions<SimpleConsoleFormatterOptions>>().Value.TimestampFormat);
    }

    /// <summary>
    /// The defaults are valid; every broken setting is named in the language of the application.
    /// </summary>
    [Fact]
    public void Options_ReportEveryProblem()
    {
        Assert.Empty(new RendezvousServerOptions().Validate());

        var broken = new RendezvousServerOptions
        {
            ListenAddress = "example.org",
            Port = 70000,
            MaxConnections = 0,
            MaxRooms = -1,
            MaxPendingPerRoom = 0,
            MaxConnectionsPerMinute = 0,
            MaxStunPerMinute = 0,
            FirstMessageTimeout = TimeSpan.Zero,
            AnswerTimeout = TimeSpan.FromHours(2),
            IdleTimeout = TimeSpan.FromMilliseconds(10),
        };
        var problems = broken.Validate();

        Assert.Equal(10, problems.Count);
        Assert.Contains(problems, p => p.Contains("Rendezvous:ListenAddress", StringComparison.Ordinal) && p.Contains("example.org", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Rendezvous:Port", StringComparison.Ordinal) && p.Contains("диапазона", StringComparison.Ordinal));
        Assert.Throws<FormatException>(() => broken.ParsedListenAddress);
    }

    /// <summary>
    /// An address gets its quota per minute, the next minute starts afresh, and addresses do not share quotas.
    /// </summary>
    [Fact]
    public void RateLimiter_CountsPerAddressAndMinute()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-24T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var limiter = new AddressRateLimiter(2, time);
        var first = IPAddress.Parse("203.0.113.1");
        var second = IPAddress.Parse("203.0.113.2");

        Assert.True(limiter.TryAcquire(first));
        Assert.True(limiter.TryAcquire(first));
        Assert.False(limiter.TryAcquire(first));
        Assert.True(limiter.TryAcquire(second));

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(limiter.TryAcquire(first));
    }

    /// <summary>
    /// A room belongs to the key that took it: the same key takes it over, another key does not, the limit holds, and
    /// only the current registration releases it.
    /// </summary>
    [Fact]
    public void Rooms_BelongToTheirKey()
    {
        var rooms = new RoomRegistry(new RendezvousServerOptions { MaxRooms = 1 });
        var owner = PeerId.FromSubjectPublicKeyInfo([1]);
        var stranger = PeerId.FromSubjectPublicKeyInfo([2]);
        var room = RendezvousPoint.CreateRoom();
        using var first = Link(room, owner);
        using var again = Link(room, owner);
        using var intruder = Link(room, stranger);
        using var other = Link(RendezvousPoint.CreateRoom(), stranger);

        Assert.Equal(RoomClaim.Registered, rooms.Claim(first, out var replaced));
        Assert.Null(replaced);
        Assert.Equal(RoomClaim.Taken, rooms.Claim(intruder, out _));
        Assert.Equal(RoomClaim.Full, rooms.Claim(other, out _));
        Assert.Equal(RoomClaim.Registered, rooms.Claim(again, out replaced));
        Assert.Same(first, replaced);
        Assert.Same(again, rooms.Find(room));

        rooms.Release(first);
        Assert.Same(again, rooms.Find(room));
        rooms.Release(again);
        Assert.Null(rooms.Find(room));
        Assert.Equal(0, rooms.Count);
    }

    /// <summary>
    /// The number of meetings waiting for one host is limited, and a meeting is answered once.
    /// </summary>
    [Fact]
    public void Meetings_AreLimitedAndAnsweredOnce()
    {
        var broker = new MeetingBroker(new RendezvousServerOptions { MaxPendingPerRoom = 1 });
        using var host = Link(RendezvousPoint.CreateRoom(), PeerId.FromSubjectPublicKeyInfo([1]));
        var participant = new IPEndPoint(IPAddress.Parse("198.51.100.4"), 5000);

        var meeting = broker.Open(host, participant, []);
        Assert.NotNull(meeting);
        Assert.Null(broker.Open(host, participant, []));
        Assert.Same(meeting, broker.Take(meeting.Id));
        Assert.Null(broker.Take(meeting.Id));

        broker.Close(meeting);
        Assert.NotNull(broker.Open(host, participant, []));
    }

    /// <summary>
    /// A STUN client learns the address of its UDP port from the server.
    /// </summary>
    [Fact]
    public async Task Stun_TellsTheAddressOfAPort()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new RendezvousServerOptions { ListenAddress = "127.0.0.1", Port = 0 };
        var statistics = new ServerStatistics(TimeProvider.System);
        using var responder = new StunResponder(options, statistics, TimeProvider.System, NullLogger<StunResponder>.Instance);
        await responder.StartAsync(token);
        try
        {
            using var socket = NatTraversal.Open(0);
            var server = "127.0.0.1:" + responder.LocalEndpoint!.Port;

            var found = await NatTraversal.DiscoverAsync(socket, [server, server], token);

            Assert.NotNull(found);
            Assert.Equal(((IPEndPoint)socket.LocalEndPoint!).Port, found.Endpoint.Port);
            Assert.True(found.IsPredictable);
            Assert.True(statistics.Snapshot().StunAnswered >= 2);
        }
        finally
        {
            await responder.StopAsync(token);
        }
    }

    /// <summary>
    /// Only binding requests are answered; other STUN messages and garbage are ignored.
    /// </summary>
    [Fact]
    public void Stun_AnswersOnlyBindingRequests()
    {
        var remote = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 40000);
        var request = new StunMessage(Transports.Relay.Stun.Binding, Transports.Relay.Stun.ClassRequest, new byte[12]).Encode();
        var allocate = new StunMessage(Transports.Relay.Stun.Allocate, Transports.Relay.Stun.ClassRequest, new byte[12]).Encode();

        var answer = StunResponder.Answer(request, remote);

        Assert.NotNull(answer);
        Assert.Equal(remote, StunMessage.Parse(answer).GetXorAddress(Transports.Relay.Stun.AttrXorMappedAddress));
        Assert.Null(StunResponder.Answer(allocate, remote));
        Assert.Null(StunResponder.Answer("GET / HTTP/1.1\r\n\r\n"u8, remote));
        Assert.Null(StunResponder.Answer(new byte[600], remote));

        // A datagram with a STUN header but a broken body passes the quick checks and fails in the parser: it is
        // ignored as well instead of stopping the responder.
        var cutAttribute = request[..24];
        cutAttribute[3] = 4;
        cutAttribute[23] = 100;
        Assert.Null(StunResponder.Answer(cutAttribute, remote));
        var oddLength = request[..21];
        oddLength[3] = 1;
        Assert.Null(StunResponder.Answer(oddLength, remote));
    }

    /// <summary>
    /// Builds a registration without a connection behind it.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="owner">The device key.</param>
    /// <returns>The registration.</returns>
    private static HostLink Link(string room, PeerId owner)
        => new(room, owner, new IPEndPoint(IPAddress.Loopback, 1), new RendezvousChannel(Stream.Null));

    /// <summary>
    /// A participant may still ask a registration whether it is closed after the host has left and the registration
    /// was released: the answer is "closed", not an exception.
    /// </summary>
    [Fact]
    public void HostLink_CanBeAskedAfterItIsReleased()
    {
        var link = Link(RendezvousPoint.CreateRoom(), PeerId.FromSubjectPublicKeyInfo([1]));
        Assert.False(link.Closed.IsCancellationRequested);

        link.Close();
        link.Dispose();

        Assert.True(link.Closed.IsCancellationRequested);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(link.Closed);
        Assert.True(linked.IsCancellationRequested);
        link.Close();
    }

    /// <summary>
    /// Senders of datagrams can forge their address, so the table of addresses has a ceiling: over it, an address
    /// not seen yet is refused, an address already counted keeps its quota, and room comes back when the old ones
    /// expire.
    /// </summary>
    [Fact]
    public void RateLimiter_DoesNotGrowWithoutEnd()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-24T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var limiter = new AddressRateLimiter(2, time);
        var bytes = new byte[4];
        for (var i = 0; i < AddressRateLimiter.MaxTracked; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, 0x0A000000 + i);
            Assert.True(limiter.TryAcquire(new IPAddress(bytes)));
        }

        var known = IPAddress.Parse("10.0.0.5");
        var newcomer = IPAddress.Parse("203.0.113.77");
        Assert.False(limiter.TryAcquire(newcomer));
        Assert.True(limiter.TryAcquire(known));
        Assert.False(limiter.TryAcquire(known));

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(limiter.TryAcquire(newcomer));
    }
}
