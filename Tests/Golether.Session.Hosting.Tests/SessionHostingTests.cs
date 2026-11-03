using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Player.Simulation;
using Golether.Media.Streaming.Caching;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Transports.Tls;

namespace Golether.Session.Hosting.Tests;

/// <summary>
/// A hosted session on loopback: it lets guests in by its own invitations, keeps to its size and gives its port back.
/// </summary>
public sealed class SessionHostingTests : IAsyncDisposable
{
    /// <summary>
    /// The device of the host.
    /// </summary>
    private readonly DeviceIdentity _hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The devices of the guests.
    /// </summary>
    private readonly List<DeviceIdentity> _guestIdentities = [];

    /// <summary>
    /// The sessions the test opened, closed at the end.
    /// </summary>
    private readonly List<IAsyncDisposable> _opened = [];

    /// <summary>
    /// Lets everybody in.
    /// </summary>
    private readonly IAdmissionPrompt _prompt = Substitute.For<IAdmissionPrompt>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionHostingTests"/> class.
    /// </summary>
    public SessionHostingTests()
        => _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(true);

    /// <summary>
    /// A guest comes in by an invitation of the hosted session, at the port the session listens on.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task StartedSession_LetsGuestInByItsInvitation()
    {
        var token = TestContext.Current.CancellationToken;
        var hosting = Open(SessionHosting.Create(Setup(port: 0, maxParticipants: SessionOptions.MAX_PARTICIPANTS_LIMIT)));
        hosting.Start();

        var invite = hosting.CreateInvite([new PeerEndpoint("127.0.0.1", hosting.Session.Port)], TimeSpan.FromMinutes(15));
        var guest = CreateGuest("Марина");
        await guest.JoinAsync(invite, token);

        Assert.Equal(hosting.Session.Port, invite.Endpoints[0].Port);
        Assert.Equal(2, hosting.Session.GetSnapshot().Participants.Count);
    }

    /// <summary>
    /// A session of a phone takes three people, the host included: the third guest is turned away, the two others stay.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SessionOfThree_TurnsTheThirdGuestAway()
    {
        var token = TestContext.Current.CancellationToken;
        var hosting = Open(SessionHosting.Create(Setup(port: 0, maxParticipants: 3)));
        hosting.Start();
        var endpoints = new[] { new PeerEndpoint("127.0.0.1", hosting.Session.Port) };

        await CreateGuest("Марина").JoinAsync(hosting.CreateInvite(endpoints, TimeSpan.FromMinutes(15)), token);
        await CreateGuest("Олег").JoinAsync(hosting.CreateInvite(endpoints, TimeSpan.FromMinutes(15)), token);
        var third = CreateGuest("Ира");
        var error = await Assert.ThrowsAsync<SessionJoinException>(() => third.JoinAsync(hosting.CreateInvite(endpoints, TimeSpan.FromMinutes(15)), token));

        Assert.Equal(Texts.Get("Session.Reject.SessionFull"), error.Message);
        Assert.Equal(3, hosting.Session.GetSnapshot().Participants.Count);
    }

    /// <summary>
    /// A port another program holds is named to the user, and no session is left behind.
    /// </summary>
    [Fact]
    public void Create_OnBusyPort_SaysSoInTheWordsOfTheUser()
    {
        using var busy = new TcpListener(IPAddress.IPv6Any, 0);
        busy.Server.DualMode = true;
        busy.Start();
        var port = ((IPEndPoint)busy.LocalEndpoint).Port;

        var error = Assert.Throws<InvalidOperationException>(() => SessionHosting.Create(Setup(port, SessionOptions.MAX_PARTICIPANTS_LIMIT)));

        // The reason of the system stands inside the text, so the text is compared up to it.
        const string REASON = "\u0001";
        var expected = Texts.Format("Session.Error.PortBusy", port, REASON);
        Assert.StartsWith(expected[..expected.IndexOf(REASON, StringComparison.Ordinal)], error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An ended session gives its port back: the next session may listen on it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Dispose_GivesThePortBack()
    {
        var first = SessionHosting.Create(Setup(port: 0, maxParticipants: 3));
        first.Start();
        var port = first.Session.Port;

        await first.DisposeAsync();
        var second = Open(SessionHosting.Create(Setup(port, maxParticipants: 3)));

        Assert.Equal(port, second.Session.Port);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var opened in Enumerable.Reverse(_opened))
        {
            await opened.DisposeAsync();
        }

        _hostIdentity.Dispose();
        foreach (var identity in _guestIdentities)
        {
            identity.Dispose();
        }
    }

    /// <summary>
    /// Describes a session of the host device on loopback.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <param name="maxParticipants">The most people, the host included.</param>
    /// <returns>The setup.</returns>
    private HostingSetup Setup(int port, int maxParticipants) => new()
    {
        Identity = _hostIdentity,
        Prompt = _prompt,
        Player = new SimulatedPlayer(SystemMonotonicClock.Instance, TimeSpan.FromHours(2)),
        TimeProvider = TimeProvider.System,
        LoggerFactory = NullLoggerFactory.Instance,
        SessionName = "Вечер кино",
        DisplayName = "Ведущий",
        Port = port,
        Options = new SessionOptions { MaxParticipants = maxParticipants },
    };

    /// <summary>
    /// Creates a guest on a device of its own, closed at the end of the test.
    /// </summary>
    /// <param name="name">The name of the guest.</param>
    /// <returns>The session of the guest, not joined yet.</returns>
    private ParticipantSession CreateGuest(string name)
    {
        var identity = DeviceIdentity.CreateNew(TimeProvider.System);
        _guestIdentities.Add(identity);

        return Open(new ParticipantSession(
            identity,
            new TlsPeerConnector(identity, new TlsTransportOptions { Port = 0, HandshakeTimeout = TimeSpan.FromSeconds(10) }),
            Substitute.For<IPlaybackController>(),
            Substitute.For<IMediaUriRegistry>(),
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            name,
            new SessionOptions()));
    }

    /// <summary>
    /// Remembers something to close at the end of the test.
    /// </summary>
    /// <typeparam name="T">What it is.</typeparam>
    /// <param name="opened">The thing.</param>
    /// <returns>The same thing.</returns>
    private T Open<T>(T opened)
        where T : IAsyncDisposable
    {
        _opened.Add(opened);
        return opened;
    }
}
