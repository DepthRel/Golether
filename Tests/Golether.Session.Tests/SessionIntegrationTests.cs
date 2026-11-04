using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player.Simulation;
using Golether.Media.Streaming.Caching;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Sync.Protocol;
using Golether.Transports;
using Golether.Transports.Tls;

namespace Golether.Session.Tests;

/// <summary>
/// End-to-end tests of a host and participants over loopback TLS with simulated players.
/// </summary>
public sealed class SessionIntegrationTests : IAsyncLifetime
{
    /// <summary>
    /// Transport options: a free port.
    /// </summary>
    private static readonly TlsTransportOptions _transport = new() { Port = 0, HandshakeTimeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Session options with a short start lead to keep the test fast.
    /// </summary>
    private static readonly SessionOptions _options = new()
    {
        Authority = new() { StartLead = TimeSpan.FromMilliseconds(500) },
        TickInterval = TimeSpan.FromMilliseconds(50),
        StatusInterval = TimeSpan.FromMilliseconds(200),
        SilenceTimeout = TimeSpan.FromSeconds(3),
    };

    /// <summary>
    /// The host device.
    /// </summary>
    private readonly DeviceIdentity _hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The participant device.
    /// </summary>
    private readonly DeviceIdentity _guestIdentity = DeviceIdentity.CreateNew(TimeProvider.System);

    /// <summary>
    /// The host prompt.
    /// </summary>
    private readonly IAdmissionPrompt _prompt = Substitute.For<IAdmissionPrompt>();

    /// <summary>
    /// The shared media file.
    /// </summary>
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), $"golether-session-{Guid.NewGuid():N}.mkv");

    /// <summary>
    /// The media content.
    /// </summary>
    private readonly byte[] _content = RandomNumberGenerator.GetBytes((4 * 1024 * 1024) + 777);

    /// <summary>
    /// The host player.
    /// </summary>
    private readonly SimulatedPlayer _hostPlayer = new(SystemMonotonicClock.Instance, TimeSpan.FromHours(2));

    /// <summary>
    /// The participant player.
    /// </summary>
    private readonly SimulatedPlayer _guestPlayer = new(SystemMonotonicClock.Instance, TimeSpan.FromHours(2));

    /// <summary>
    /// The registry that captures the participant media stream.
    /// </summary>
    private readonly CapturingRegistry _registry = new();

    /// <summary>
    /// The conference backend of the host.
    /// </summary>
    private readonly FakeConference _hostConference = new();

    /// <summary>
    /// The conference backend of the participant.
    /// </summary>
    private readonly FakeConference _guestConference = new();

    /// <summary>
    /// The host session.
    /// </summary>
    private HostSession _host = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await File.WriteAllBytesAsync(_mediaPath, _content);
        _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var listener = new TlsPeerListener(_hostIdentity, _transport);
        listener.Start();
        _host = new HostSession(
            _hostIdentity,
            listener,
            new InviteRegistry(TimeProvider.System),
            new AdmissionService(_prompt, new AdmissionOptions(), TimeProvider.System),
            contacts: null,
            _hostPlayer,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Вечер кино",
            "Вы",
            _options,
            conference: _hostConference);
        _host.Start();
    }

    /// <summary>
    /// Cameras: the host connects to the admitted participant and relays signaling in both directions with the
    /// authenticated source.
    /// </summary>
    [Fact]
    public async Task Conference_ConnectsAndRelaysSignals()
    {
        var token = TestContext.Current.CancellationToken;
        _host.Relay = new Transports.Relay.RelayCredentials(47999, "golether-x", "secret_1");
        await using var guest = CreateGuest();
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        Assert.Equal("turn://golether-x:secret_1@127.0.0.1:47999?transport=tcp", _guestConference.Relay);
        Assert.Null(_hostConference.Relay);

        await WaitUntilAsync(() => _hostConference.Connected.ContainsKey(_guestIdentity.PeerId) && _guestConference.Connected.ContainsKey(_hostIdentity.PeerId), token);
        var hostOffers = string.CompareOrdinal(_hostIdentity.PeerId.Value, _guestIdentity.PeerId.Value) < 0;
        Assert.Equal(hostOffers, _hostConference.Connected[_guestIdentity.PeerId]);
        Assert.Equal(!hostOffers, _guestConference.Connected[_hostIdentity.PeerId]);
        Assert.True(_hostConference.CaptureStarted);
        Assert.True(_guestConference.CaptureStarted);

        _hostConference.Emit(new ConferenceSignal(_guestIdentity.PeerId, "offer", "v=0 from host"));
        _guestConference.Emit(new ConferenceSignal(_hostIdentity.PeerId, "answer", "v=0 from guest"));

        await WaitUntilAsync(() => !_guestConference.Received.IsEmpty && !_hostConference.Received.IsEmpty, token);
        Assert.Equal(new ConferenceSignal(_hostIdentity.PeerId, "offer", "v=0 from host"), _guestConference.Received.Single());
        Assert.Equal(new ConferenceSignal(_guestIdentity.PeerId, "answer", "v=0 from guest"), _hostConference.Received.Single());

        await guest.DisposeAsync();
        await WaitUntilAsync(() => _hostConference.Connected.IsEmpty, token);
        Assert.True(_guestConference.Stopped);
        Assert.Null(_guestConference.Relay);
    }

    /// <summary>
    /// The cameras of the host stop answering in the middle of a negotiation: the chat and the goodbye of the
    /// participant still reach the host, which reads the control channel without waiting for the cameras.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HungCameras_DoNotHoldTheControlChannel()
    {
        var token = TestContext.Current.CancellationToken;
        _hostConference.HangsOnSignals = true;
        var hostLines = new ConcurrentQueue<ChatEntry>();
        _host.ChatReceived += (_, e) => hostLines.Enqueue(e);
        var guest = CreateGuest();
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        _guestConference.Emit(new ConferenceSignal(_hostIdentity.PeerId, "offer", "v=0 from guest"));
        await WaitUntilAsync(() => !_hostConference.Received.IsEmpty, token);
        await guest.SendChatAsync(ChatKind.Text, "Привет", token);

        await WaitUntilAsync(() => hostLines.Any(e => e.Text == "Привет"), token);
        await guest.DisposeAsync();
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 1, token);
    }

    /// <summary>
    /// A participant whose connection broke without a word (it stays open, but nothing comes) is let go after the
    /// silence limit instead of hanging in the session.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SilentParticipant_IsLetGo()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        var connector = new TlsPeerConnector(_guestIdentity, _transport);
        await using var stream = await connector.ConnectAsync(new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.Control, token);
        await using var channel = new SessionMessageChannel(stream.Stream);
        await EnterByHandAsync(channel, invite.Token, token);

        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);

        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 1, token);
    }

    /// <summary>
    /// The host switches the microphone and then the camera of a participant off: the participant applies it, keeps
    /// the other device, reports the state, and is told that only they can switch the devices on again.
    /// </summary>
    [Fact]
    public async Task Host_SwitchesParticipantDevicesOffOnly()
    {
        var token = TestContext.Current.CancellationToken;
        await using var guest = CreateGuest();
        var events = new ConcurrentQueue<string>();
        guest.EventRaised += (_, e) => events.Enqueue(e.Text);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        await WaitUntilAsync(() => HostSees(s => s is { MicrophoneOff: false, CameraOff: false }), token);

        await _host.SwitchOffParticipantDevicesAsync(_guestIdentity.PeerId, microphone: true, camera: false);

        await WaitUntilAsync(() => _guestConference.MicrophoneMuted, token);
        Assert.False(_guestConference.CameraOff);
        await WaitUntilAsync(() => HostSees(s => s is { MicrophoneOff: true, CameraOff: false }), token);
        Assert.Contains(events, e => e.Contains("выключил ваш микрофон", StringComparison.Ordinal));

        // The participant switches the microphone on again; a camera request leaves it on.
        _guestConference.SetMuted(false, false);
        await _host.SwitchOffParticipantDevicesAsync(_guestIdentity.PeerId, microphone: false, camera: true);
        await WaitUntilAsync(() => _guestConference.CameraOff, token);
        Assert.False(_guestConference.MicrophoneMuted);

        // Requests about the host itself or about strangers do nothing.
        await _host.SwitchOffParticipantDevicesAsync(_hostIdentity.PeerId, true, true);
        await _host.SwitchOffParticipantDevicesAsync(PeerIdOf(DeviceIdentity.CreateNew(TimeProvider.System)), true, true);
        Assert.False(_hostConference.MicrophoneMuted);

        bool HostSees(Func<Core.Session.ParticipantStatus, bool> condition)
            => _host.GetSnapshot().Participants.FirstOrDefault(p => p.Info.PeerId == _guestIdentity.PeerId)?.Status is { } status && condition(status);
    }

    /// <summary>
    /// A participant comes back to the session with the ticket instead of a new invitation; the host is asked again,
    /// and a forged ticket is refused.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Participant_ComesBackWithTicket()
    {
        var token = TestContext.Current.CancellationToken;
        await using var first = CreateGuest();
        await first.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        var ticket = first.ReconnectInvite;
        Assert.NotNull(ticket);
        Assert.NotEqual(string.Empty, ticket.Token);
        await first.DisposeAsync();
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 1, token);

        // The same device is let in again without an invitation, and the host is asked once more.
        await using var again = CreateGuest();
        await again.RejoinAsync(ticket, token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);
        await _prompt.Received(2).AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>());
        Assert.Equal(ticket.Token, again.ReconnectInvite?.Token);

        // A ticket that was not issued is worthless.
        await using var stranger = CreateGuest();
        await Assert.ThrowsAsync<SessionJoinException>(() => stranger.RejoinAsync(ticket with { Token = new string('f', 48) }, token));
    }

    /// <summary>
    /// After a broken connection the participant comes back by itself, and the application is told how it goes.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_BringsTheParticipantBack()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, ticket);

        var restored = await CreateReconnector(client).RunAsync(ended, token);

        Assert.True(restored);
        await using var session = client.Adopted;
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);
        Assert.Equal(
            [
                Texts.Format("Session.Reconnect.Lost", 1, 8, "2"),
                Texts.Format("Session.Reconnect.Trying", 1, 8),
                Texts.Get("Session.Reconnect.Restored"),
            ],
            client.Reports);
    }

    /// <summary>
    /// When the network comes back the next attempt starts at once: the user does not sit out a wait of half a minute.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_Retry_CutsTheWaitShort()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, ticket);
        var reconnector = new ParticipantReconnector(client, new FrozenClock(), NullLogger<ParticipantReconnector>.Instance);

        var running = reconnector.RunAsync(ended, token);
        reconnector.Retry();

        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(30), token));
        await using var session = client.Adopted;
    }

    /// <summary>
    /// A return of the network between two waits is not lost: the wait after it does not start.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_RetryBeforeTheWait_IsKept()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, ticket);
        var reconnector = new ParticipantReconnector(client, new FrozenClock(), NullLogger<ParticipantReconnector>.Instance);

        reconnector.Retry();

        Assert.True(await reconnector.RunAsync(ended, token).WaitAsync(TimeSpan.FromSeconds(30), token));
        await using var session = client.Adopted;
    }

    /// <summary>
    /// A host that answered "no" is not asked again: the attempts stop at once.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_StopsWhenTheHostRefuses()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, ticket with { Token = new string('f', 48) });

        var restored = await CreateReconnector(client).RunAsync(ended, token);

        Assert.False(restored);
        Assert.Single(client.Created);
        Assert.Equal(Texts.Get("Session.Reconnect.Failed"), client.Reports.Last());
    }

    /// <summary>
    /// A host that can not be reached is tried a limited number of times, and no half-open session stays behind.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_GivesUpOnAnUnreachableHost()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateUnreachableGuest, ticket);

        var restored = await CreateReconnector(client).RunAsync(ended, token);

        Assert.False(restored);
        Assert.Equal(8, client.Created.Count);
        Assert.Equal(Texts.Get("Session.Reconnect.Failed"), client.Reports.Last());
        Assert.All(client.Created, session => Assert.Equal(SessionState.Ended, session.GetSnapshot().State));
    }

    /// <summary>
    /// When the user has left, nothing is started for a session nobody wants.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_StopsWhenTheUserLeft()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, _) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, null);

        var restored = await CreateReconnector(client).RunAsync(ended, token);

        Assert.False(restored);
        Assert.Empty(client.Created);
    }

    /// <summary>
    /// When the user left while the host was letting the device in, the restored session is closed again.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_ClosesASessionNobodyTook()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, ticket, adopts: false);

        var restored = await CreateReconnector(client).RunAsync(ended, token);

        Assert.False(restored);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 1, token);
    }

    /// <summary>
    /// Stopping the attempts is not a failure the user has to read about.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Reconnector_Cancelled_DoesNotReportAFailure()
    {
        var token = TestContext.Current.CancellationToken;
        var (ended, ticket) = await JoinAndLoseAsync(token);
        var client = new ReconnectingClient(CreateGuest, ticket);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateReconnector(client).RunAsync(ended, cancellation.Token));

        Assert.DoesNotContain(Texts.Get("Session.Reconnect.Failed"), client.Reports);
    }

    /// <summary>
    /// Chat lines and reactions reach everybody with the authenticated sender; a forged sender is replaced, bad
    /// messages are dropped, and a flood is limited.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Chat_IsRelayedWithAuthenticatedSender()
    {
        var token = TestContext.Current.CancellationToken;
        await using var guest = CreateGuest();
        var hostLines = new ConcurrentQueue<ChatEntry>();
        var guestLines = new ConcurrentQueue<ChatEntry>();
        _host.ChatReceived += (_, e) => hostLines.Enqueue(e);
        guest.ChatReceived += (_, e) => guestLines.Enqueue(e);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        await guest.SendChatAsync(ChatKind.Text, "  Привет!‮  ", token);
        await _host.SendChatAsync(ChatKind.Reaction, "🔥");
        await WaitUntilAsync(() => hostLines.Count == 2 && guestLines.Count == 2, token);

        var fromGuest = hostLines.Single(e => e.Kind == ChatKind.Text);
        Assert.Equal(("Привет!", _guestIdentity.PeerId, false), (fromGuest.Text, fromGuest.Sender, fromGuest.IsLocal));
        Assert.True(guestLines.Single(e => e.Kind == ChatKind.Text).IsLocal);
        var reaction = guestLines.Single(e => e.Kind == ChatKind.Reaction);
        Assert.Equal(("🔥", _hostIdentity.PeerId, "Вы", false), (reaction.Text, reaction.Sender, reaction.SenderName, reaction.IsLocal));
        Assert.True(hostLines.Single(e => e.Kind == ChatKind.Reaction).IsLocal);

        // A participant cannot speak for the host or send unknown reactions.
        var channel = (SessionMessageChannel)typeof(ParticipantSession)
            .GetField("_channel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(guest)!;
        await channel.SendAsync(new ChatMessage("AB12", ChatKind.Text, "я ведущий", _hostIdentity.PeerId), token);
        await channel.SendAsync(new ChatMessage("AB13", ChatKind.Reaction, "💩", null), token);
        await channel.SendAsync(new ChatMessage("not hex!", ChatKind.Text, "плохой id", null), token);
        await WaitUntilAsync(() => hostLines.Count == 3, token);
        Assert.Equal(_guestIdentity.PeerId, hostLines.Last().Sender);

        // A flood: five lines per five seconds pass (two were sent already).
        for (var i = 0; i < 8; i++)
        {
            await guest.SendChatAsync(ChatKind.Text, $"спам {i}", token);
        }

        await WaitUntilAsync(() => guestLines.Count(e => e.Kind == ChatKind.Text) >= 5, token);
        await Task.Delay(300, token);
        Assert.Equal(3, hostLines.Count(e => e.Text.StartsWith("спам", StringComparison.Ordinal)));
        Assert.DoesNotContain(hostLines, e => e.Text is "плохой id" || e.Kind == ChatKind.Reaction && e.Text == "💩");
    }

    /// <summary>
    /// An avatar reaches everybody with the authenticated owner, comes back to its sender, and a participant who joins
    /// later is sent the avatars already chosen.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Avatar_IsRelayedAndSentToLateJoiners()
    {
        var token = TestContext.Current.CancellationToken;
        byte[] Png(byte tag) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, tag, tag];
        var hostAvatars = new ConcurrentQueue<AvatarUpdate>();
        var guestAvatars = new ConcurrentQueue<AvatarUpdate>();
        _host.AvatarReceived += (_, e) => hostAvatars.Enqueue(e);
        await using var guest = CreateGuest();
        guest.AvatarReceived += (_, e) => guestAvatars.Enqueue(e);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        // The host chooses an avatar; the participant sees it. The participant chooses one; it comes back to them and
        // the host sees it with the authenticated owner.
        await _host.SetAvatarAsync(Png(1));
        await guest.SendAvatarAsync(Png(2), token);
        await WaitUntilAsync(() => guestAvatars.Count == 2 && hostAvatars.Any(a => a.Peer == _guestIdentity.PeerId), token);

        var hostAvatarAtGuest = guestAvatars.Single(a => a.Peer == _hostIdentity.PeerId);
        Assert.Equal(Png(1), hostAvatarAtGuest.Image);
        Assert.False(hostAvatarAtGuest.IsLocal);
        var ownAtGuest = guestAvatars.Single(a => a.Peer == _guestIdentity.PeerId);
        Assert.Equal(Png(2), ownAtGuest.Image);
        Assert.True(ownAtGuest.IsLocal);
        Assert.Equal(Png(2), hostAvatars.Single(a => a.Peer == _guestIdentity.PeerId).Image);

        // A participant who joins now (with its own identity) is sent both avatars that were already chosen.
        using var secondIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var secondPlayer = new SimulatedPlayer(SystemMonotonicClock.Instance, TimeSpan.FromHours(2));
        await using var second = new ParticipantSession(
            secondIdentity,
            new TlsPeerConnector(secondIdentity, _transport),
            secondPlayer,
            new CapturingRegistry(),
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Олег",
            _options,
            conference: new FakeConference());
        var secondAvatars = new ConcurrentQueue<AvatarUpdate>();
        second.AvatarReceived += (_, e) => secondAvatars.Enqueue(e);
        await second.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        await WaitUntilAsync(() => secondAvatars.Count(a => a.Peer == _hostIdentity.PeerId || a.Peer == _guestIdentity.PeerId) == 2, token);
        Assert.Equal(Png(1), secondAvatars.Single(a => a.Peer == _hostIdentity.PeerId).Image);
        Assert.Equal(Png(2), secondAvatars.Single(a => a.Peer == _guestIdentity.PeerId).Image);
    }

    /// <summary>
    /// The strokes of the pen reach everybody with the authenticated sender; a forged sender is replaced and broken
    /// points are dropped.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Strokes_AreRelayedWithAuthenticatedSender()
    {
        var token = TestContext.Current.CancellationToken;
        await using var guest = CreateGuest();
        var hostStrokes = new ConcurrentQueue<StrokeUpdate>();
        var guestStrokes = new ConcurrentQueue<StrokeUpdate>();
        _host.DrawReceived += (_, e) => hostStrokes.Enqueue(e);
        guest.DrawReceived += (_, e) => guestStrokes.Enqueue(e);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        var strokeId = DrawMessage.CreateStrokeId();
        await guest.SendDrawAsync(strokeId, StrokePhase.Start, [new StrokePoint(-1f, 0.5f)], token);
        await guest.SendDrawAsync(strokeId, StrokePhase.End, [], token);
        await WaitUntilAsync(() => hostStrokes.Count == 2 && guestStrokes.Count == 2, token);

        var start = hostStrokes.First();
        Assert.Equal((strokeId, StrokePhase.Start, _guestIdentity.PeerId, false), (start.StrokeId, start.Phase, start.Sender, start.IsLocal));
        Assert.Equal(new StrokePoint(0, 0.5f), Assert.Single(start.Points));
        Assert.True(guestStrokes.First().IsLocal);
        Assert.Equal(StrokePhase.End, hostStrokes.Last().Phase);

        // The host draws too, and its stroke reaches the participant.
        await _host.SendDrawAsync("AB12", StrokePhase.Start, [new StrokePoint(0.25f, 0.25f)]);
        await WaitUntilAsync(() => guestStrokes.Count == 3, token);
        Assert.Equal((_hostIdentity.PeerId, false), (guestStrokes.Last().Sender, guestStrokes.Last().IsLocal));

        // A participant cannot draw in the name of the host, and a broken message is dropped.
        var channel = (SessionMessageChannel)typeof(ParticipantSession)
            .GetField("_channel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(guest)!;
        await channel.SendAsync(new DrawMessage("CD34", StrokePhase.Start, [new StrokePoint(0.5f, 0.5f)], _hostIdentity.PeerId), token);
        await channel.SendAsync(new DrawMessage("not hex!", StrokePhase.Start, [new StrokePoint(0.5f, 0.5f)], null), token);
        await WaitUntilAsync(() => hostStrokes.Count == 4, token);
        await Task.Delay(300, token);
        Assert.Equal(4, hostStrokes.Count);
        Assert.Equal((_guestIdentity.PeerId, "CD34"), (hostStrokes.Last().Sender, hostStrokes.Last().StrokeId));
    }

    /// <summary>
    /// A second participant gets the file from the first one over the data channel; the host only answers hashes.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Participants_ShareChunks()
    {
        var token = TestContext.Current.CancellationToken;
        await _host.ShareMediaAsync(_mediaPath, token);
        using var secondIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        var secondConference = new FakeConference();
        var secondRegistry = new CapturingRegistry();
        await using var secondPlayer = new SimulatedPlayer(SystemMonotonicClock.Instance, TimeSpan.FromHours(2));
        _guestConference.DataPeersSource = () => [secondIdentity.PeerId];
        _guestConference.DataSink = (_, data) => secondConference.ReceiveData(_guestIdentity.PeerId, data);
        secondConference.DataPeersSource = () => [_guestIdentity.PeerId];
        secondConference.DataSink = (_, data) => _guestConference.ReceiveData(secondIdentity.PeerId, data);

        await using var first = CreateGuest();
        await first.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        Assert.Equal(_content, await ReadAllAsync(_registry, token));
        var chunks = (int)(((_content.Length - 1) / Golether.Core.Media.MediaDescriptor.DEFAULT_CHUNK_SIZE) + 1);
        // The host counts a piece after sending it, so the count may trail the last piece by a moment.
        await WaitUntilAsync(() => _host.ChunksServed >= chunks, token);
        Assert.Equal(chunks, _host.ChunksServed);

        await using var second = new ParticipantSession(
            secondIdentity,
            new TlsPeerConnector(secondIdentity, _transport),
            secondPlayer,
            secondRegistry,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Олег",
            _options,
            conference: secondConference);
        await second.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        // The first participant announces its chunks every two seconds.
        await Task.Delay(TimeSpan.FromSeconds(2.5), token);
        Assert.Equal(_content, await ReadAllAsync(secondRegistry, token));

        // A participant uploads two pieces at a time to another one; what is asked for beyond that at the same
        // moment comes from the host. So the host may serve a few more pieces, but most come from the participant,
        // and what the first participant sent is what the second one got from it.
        await WaitUntilAsync(() => _host.HashesServed + (_host.ChunksServed - chunks) >= chunks, token);
        var fromHost = (int)(_host.ChunksServed - chunks);
        Assert.InRange(fromHost, 0, chunks / 2);
        Assert.True(_host.HashesServed >= chunks - fromHost, $"The host answered {_host.HashesServed} hashes.");
        var local = second.GetSnapshot().Participants.Single(p => p.IsLocal).Status!;
        Assert.InRange(local.BytesFromPeers, _content.Length / 2, _content.Length);
        await WaitUntilAsync(() => first.GetSnapshot().Participants.Single(p => p.IsLocal).Status!.BytesToPeers == local.BytesFromPeers, token);
    }

    /// <summary>
    /// Reads the latest media stream of a registry to the end.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes.</returns>
    private static async Task<byte[]> ReadAllAsync(CapturingRegistry registry, CancellationToken cancellationToken)
    {
        using var stream = registry.Open();
        var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    /// <summary>
    /// Returns the identifier of a device and releases its key.
    /// </summary>
    /// <param name="identity">The device.</param>
    /// <returns>The identifier.</returns>
    private static Core.Identity.PeerId PeerIdOf(DeviceIdentity identity)
    {
        using (identity)
        {
            return identity.PeerId;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _hostIdentity.Dispose();
        _guestIdentity.Dispose();
        File.Delete(_mediaPath);
    }

    /// <summary>
    /// One invitation is for the whole company: several guests join with the same link. Removing one of them does
    /// not cancel the link for the others; it is the removed device that the link no longer lets in.
    /// </summary>
    [Fact]
    public async Task SharedInvitation_LetsTheCompanyInButNotTheRemovedGuest()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        using var secondIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        using var thirdIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var first = CreateGuest();
        await using var second = CreateGuest(secondIdentity, "Олег");

        await first.JoinAsync(invite, token);
        await second.JoinAsync(invite, token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 3, token);

        await _host.RemoveParticipantAsync(_guestIdentity.PeerId);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);

        // The same link: refused to the removed device, with the reason it was removed for...
        await using (var again = CreateGuest())
        {
            var refusal = await Assert.ThrowsAsync<SessionJoinException>(() => again.JoinAsync(invite, token));
            Assert.Equal(SessionTexts.DescribeRejection(RejectReason.Removed), refusal.Message);
        }

        // ...and still good for a friend who comes late.
        await using var third = CreateGuest(thirdIdentity, "Вера");
        await third.JoinAsync(invite, token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 3, token);
        Assert.Equal(SessionState.Active, second.GetSnapshot().State);

        // A link made after the removal lets the removed device in again.
        await using var forgiven = CreateGuest();
        await forgiven.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 4, token);
    }

    /// <summary>
    /// Two guests who waited for the host at the same time cannot both take the last place: the place is counted
    /// again when the host has said yes, and the second guest is told that the session is full.
    /// </summary>
    [Fact]
    public async Task LastPlace_GoesToOneOfTwoApprovedGuests()
    {
        var token = TestContext.Current.CancellationToken;
        var approve = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = Substitute.For<IAdmissionPrompt>();
        prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(_ => approve.Task);
        var listener = new TlsPeerListener(_hostIdentity, _transport);
        listener.Start();
        await using var host = new HostSession(
            _hostIdentity,
            listener,
            new InviteRegistry(TimeProvider.System),
            new AdmissionService(prompt, new AdmissionOptions(), TimeProvider.System),
            contacts: null,
            new SimulatedPlayer(SystemMonotonicClock.Instance, TimeSpan.FromHours(2)),
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Вечер кино",
            "Вы",
            _options with { MaxParticipants = 2 });
        host.Start();
        var invite = host.CreateInvite([new PeerEndpoint("127.0.0.1", host.Port)]);
        using var secondIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var first = CreateGuest(_guestIdentity, "Марина");
        await using var second = CreateGuest(secondIdentity, "Олег");

        // Both are past the door, where the place was still free, before the host answers.
        Task[] joins = [first.JoinAsync(invite, token), second.JoinAsync(invite, token)];
        await WaitUntilAsync(
            () => first.GetSnapshot().State == SessionState.AwaitingApproval && second.GetSnapshot().State == SessionState.AwaitingApproval,
            token);
        approve.SetResult(true);
        var outcomes = await Task.WhenAll(joins.Select(async join =>
        {
            try
            {
                await join;
                return (string?)null;
            }
            catch (SessionJoinException ex)
            {
                return ex.Message;
            }
        }));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome == SessionTexts.DescribeRejection(RejectReason.SessionFull));
        Assert.Equal(2, host.GetSnapshot().Participants.Count);
    }

    /// <summary>
    /// Every playback request moves the player of the host and goes to everybody, so a flood of them from one
    /// participant is cut to a few a second.
    /// </summary>
    [Fact]
    public async Task PlaybackRequests_AreLimited()
    {
        var token = TestContext.Current.CancellationToken;
        await using var guest = CreateGuest();
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        var before = _host.GetSnapshot().Playback!.Version;
        var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.ChatReceived += (_, _) => after.TrySetResult();

        for (var second = 1; second <= 30; second++)
        {
            await guest.RequestAsync(new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromSeconds(second)), token);
        }

        // The chat line travels behind the requests: when it arrives, the host has read all of them.
        await guest.SendChatAsync(ChatKind.Text, "Всё", token);
        await after.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        var applied = _host.GetSnapshot().Playback!.Version - before;
        Assert.InRange(applied, 1, 10);
    }

    /// <summary>
    /// A guest the host declined cannot keep asking with the shared link: the host is not asked again until the
    /// guest has a newer invitation. A host who simply did not answer has declined nobody.
    /// </summary>
    [Fact]
    public async Task DeclinedGuest_NeedsANewInvitation()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        await using (var declined = CreateGuest())
        {
            await Assert.ThrowsAsync<SessionJoinException>(() => declined.JoinAsync(invite, token));
        }

        _prompt.ClearReceivedCalls();
        _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        await using (var again = CreateGuest())
        {
            var refusal = await Assert.ThrowsAsync<SessionJoinException>(() => again.JoinAsync(invite, token));
            Assert.Equal(SessionTexts.DescribeRejection(RejectReason.Declined), refusal.Message);
        }

        await _prompt.DidNotReceive().AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>());

        await using var invited = CreateGuest();
        await invited.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);
    }

    /// <summary>
    /// A participant removed by the host is told so and cannot come back by itself: neither the ticket of the session
    /// nor the invitation it joined with lets it in. It is not banned either: a new invitation works.
    /// </summary>
    [Fact]
    public async Task RemovedParticipant_NeedsANewInvitation()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        await using var guest = CreateGuest();
        await guest.JoinAsync(invite, token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);
        var ticket = guest.ReconnectInvite;
        Assert.NotNull(ticket);

        await _host.RemoveParticipantAsync(_guestIdentity.PeerId);

        await WaitUntilAsync(() => guest.GetSnapshot().State == SessionState.Ended, token);
        Assert.True(guest.ClosedByHost);
        Assert.Equal(SessionTexts.DescribeRejection(RejectReason.Removed), guest.EndReason);
        Assert.Single(_host.GetSnapshot().Participants);

        // The ticket that would have brought the device back after a broken connection is gone.
        await using (var returning = CreateGuest())
        {
            await Assert.ThrowsAsync<SessionJoinException>(() => returning.RejoinAsync(ticket, token));
        }

        // The invitation it came with still exists, but the host remembers whom it turned away.
        await using (var again = CreateGuest())
        {
            await Assert.ThrowsAsync<SessionJoinException>(() => again.JoinAsync(invite, token));
        }

        Assert.Single(_host.GetSnapshot().Participants);

        // A new invitation from the host lets the same device in again.
        await using var invited = CreateGuest();
        await invited.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);
        Assert.Equal(SessionState.Active, invited.GetSnapshot().State);
    }

    /// <summary>
    /// The lifetime of an invitation limits only the moment of joining: a participant who was admitted stays in the
    /// session after the invitation expired, keeps exchanging messages with the host, and nobody else gets in with it.
    /// </summary>
    [Fact]
    public async Task Participant_StaysAfterTheInvitationExpired()
    {
        var token = TestContext.Current.CancellationToken;
        var lifetime = TimeSpan.FromSeconds(1);
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)], lifetime);
        await using var guest = CreateGuest();
        await guest.JoinAsync(invite, token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);

        await Task.Delay(lifetime + TimeSpan.FromSeconds(1), token);
        Assert.True(invite.ExpiresAt < DateTimeOffset.UtcNow);

        // The session goes on in both directions after the expiry.
        var received = new TaskCompletionSource<ChatEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.ChatReceived += (_, entry) => received.TrySetResult(entry);
        await guest.SendChatAsync(ChatKind.Text, "всё ещё здесь", token);
        Assert.Equal("всё ещё здесь", (await received.Task.WaitAsync(TimeSpan.FromSeconds(10), token)).Text);
        Assert.Equal(SessionState.Active, guest.GetSnapshot().State);
        Assert.Equal(2, _host.GetSnapshot().Participants.Count);

        // The expired invitation itself lets nobody in any more.
        using var lateIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var late = new ParticipantSession(
            lateIdentity,
            new TlsPeerConnector(lateIdentity, _transport),
            new SimulatedPlayer(SystemMonotonicClock.Instance, TimeSpan.FromHours(2)),
            new CapturingRegistry(),
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Опоздавший",
            _options);
        await Assert.ThrowsAsync<SessionJoinException>(() => late.JoinAsync(invite, token));
        Assert.Equal(2, _host.GetSnapshot().Participants.Count);
    }

    /// <summary>
    /// A participant joins after approval, receives the media, starts together with the host, and its pause stops the
    /// host on the same frame.
    /// </summary>
    [Fact]
    public async Task Participant_JoinsFollowsAndControls()
    {
        var token = TestContext.Current.CancellationToken;
        await _host.ShareMediaAsync(_mediaPath, token);
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        await using var guest = CreateGuest();

        await guest.JoinAsync(invite, token);

        var snapshot = guest.GetSnapshot();
        Assert.Equal(SessionState.Active, snapshot.State);
        Assert.Equal("Вечер кино", snapshot.SessionName);
        Assert.Equal(_content.Length, snapshot.Media!.Length);
        await _prompt.Received(1).AskAsync(
            Arg.Is<AdmissionRequest>(r => r.PeerId == _guestIdentity.PeerId && r.DisplayName == "Марина" &&
                                          r.VerificationCode.ToString() == snapshot.VerificationCode!.ToString()),
            Arg.Any<CancellationToken>());
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);

        // The participant reads the original bytes through the host.
        using (var stream = _registry.Open())
        {
            var copy = new MemoryStream();
            await stream.CopyToAsync(copy, token);
            Assert.Equal(_content, copy.ToArray());
        }

        // The host starts: both players start after the scheduled delay, at nearly the same position.
        await _host.RequestAsync(new PlaybackRequest(PlaybackRequestKind.Play, null), token);
        await WaitUntilAsync(() => !_hostPlayer.GetSnapshot().IsPaused && !_guestPlayer.GetSnapshot().IsPaused, token);
        await Task.Delay(300, token);
        var difference = (_hostPlayer.GetSnapshot().Position!.Value - _guestPlayer.GetSnapshot().Position!.Value).Duration();
        Assert.True(difference < TimeSpan.FromMilliseconds(200), $"Players differ by {difference}.");

        // The participant pauses: the host stops on the frame the participant chose.
        var frame = TimeSpan.FromSeconds(42);
        await guest.RequestAsync(new PlaybackRequest(PlaybackRequestKind.Pause, frame), token);
        await WaitUntilAsync(() => _host.GetSnapshot().Playback!.State == PlayState.Paused, token);

        var hostState = _host.GetSnapshot().Playback!;
        Assert.Equal(frame, hostState.Position);
        Assert.Equal(_guestIdentity.PeerId, hostState.Origin);
        await WaitUntilAsync(() => _hostPlayer.GetSnapshot().IsPaused && _hostPlayer.GetSnapshot().Position == frame, token);
        await WaitUntilAsync(() => _guestPlayer.GetSnapshot().Position == frame, token);
    }

    /// <summary>
    /// An invitation the host never made is refused, a guest who left may come back with the invitation of the
    /// company, and a rejected participant gets the reason.
    /// </summary>
    [Fact]
    public async Task Admission_RejectsForeignInviteAndDeclinedPeer()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        await using (var first = CreateGuest())
        {
            await first.JoinAsync(invite, token);
        }

        // Leaving by oneself is not being turned away: the same invitation lets the guest in again while it lives.
        await using (var back = CreateGuest())
        {
            await back.JoinAsync(invite, token);
        }

        await using var second = CreateGuest();
        var forged = await Assert.ThrowsAsync<SessionJoinException>(() => second.JoinAsync(invite with { Token = Invite.CreateToken() }, token));
        Assert.Contains("недействительно", forged.Message, StringComparison.Ordinal);

        _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        await using var declined = CreateGuest();
        var error = await Assert.ThrowsAsync<SessionJoinException>(() =>
            declined.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token));
        Assert.Contains("отклонил", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host sends the reason of a rejection, not a text: the host of the test speaks Russian, and the participant
    /// reads the reason in English, the language of its own interface.
    /// </summary>
    [Fact]
    public async Task Rejection_IsWordedInTheLanguageOfTheParticipant()
    {
        var token = TestContext.Current.CancellationToken;
        _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);

        using var english = Texts.Scope(new Localizer(LanguageCatalog.LoadEmbedded(), "en"));
        await using var declined = CreateGuest();
        var error = await Assert.ThrowsAsync<SessionJoinException>(() => declined.JoinAsync(invite, token));

        Assert.Equal("The host declined the request. To try again, ask them for a new invitation.", error.Message);
    }

    /// <summary>
    /// A participant that expects another host key refuses to connect.
    /// </summary>
    [Fact]
    public async Task Join_DetectsImpostorHost()
    {
        using var other = DeviceIdentity.CreateNew(TimeProvider.System);
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]) with { HostPeerId = other.PeerId };
        await using var guest = CreateGuest();

        var error = await Assert.ThrowsAsync<SessionJoinException>(() => guest.JoinAsync(invite, TestContext.Current.CancellationToken));

        Assert.Contains("подмена", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A device with a valid TLS key but without admission cannot open a media data stream: the host closes it
    /// without sending a byte.
    /// </summary>
    [Fact]
    public async Task MediaStream_OfStranger_IsClosed()
    {
        var token = TestContext.Current.CancellationToken;
        await _host.ShareMediaAsync(_mediaPath, token);
        using var stranger = DeviceIdentity.CreateNew(TimeProvider.System);
        var connector = new TlsPeerConnector(stranger, _transport);

        await using var stream = await connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.MediaData, token);

        Assert.Equal(0, await ReadUntilClosedAsync(stream.Stream, token).WaitAsync(TimeSpan.FromSeconds(10), token));
    }

    /// <summary>
    /// Removing a participant closes the media data streams it already opened.
    /// </summary>
    [Fact]
    public async Task MediaStream_OfRemovedParticipant_IsClosed()
    {
        var token = TestContext.Current.CancellationToken;
        await _host.ShareMediaAsync(_mediaPath, token);
        await using var guest = CreateGuest();
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 2, token);
        var connector = new TlsPeerConnector(_guestIdentity, _transport);
        await using var stream = await connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.MediaData, token);
        var reading = ReadUntilClosedAsync(stream.Stream, token);
        await Task.Delay(300, token);
        Assert.False(reading.IsCompleted, "The admitted participant keeps its stream.");

        await _host.RemoveParticipantAsync(_guestIdentity.PeerId);

        Assert.Equal(0, await reading.WaitAsync(TimeSpan.FromSeconds(10), token));
    }

    /// <summary>
    /// Reads until the remote side closes the stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of bytes received before the stream was closed.</returns>
    private static async Task<int> ReadUntilClosedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var total = 0;
        var buffer = new byte[4096];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
            }
        }
        catch (IOException)
        {
            // An abortive close counts as closed.
        }

        return total;
    }

    /// <summary>
    /// Goes through the door of the host by hand, as a participant does: the hello with the commitment, the number
    /// once the host named its own, and the welcome.
    /// </summary>
    /// <param name="channel">The control channel of the participant.</param>
    /// <param name="inviteToken">The token to present.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the welcome arrived.</returns>
    private static async Task EnterByHandAsync(SessionMessageChannel channel, string inviteToken, CancellationToken cancellationToken)
    {
        var nonce = SasCommitment.CreateNonce();
        await channel.SendAsync(new HelloMessage(SessionMessage.PROTOCOL_VERSION, "Марина", inviteToken) { SasCommit = SasCommitment.Commit(nonce) }, cancellationToken);
        while (true)
        {
            var message = await channel.ReceiveAsync(cancellationToken);
            Assert.NotNull(message);
            if (message is WelcomeMessage)
            {
                return;
            }

            if (message is PendingApprovalMessage)
            {
                await channel.SendAsync(new SasRevealMessage(nonce), cancellationToken);
            }
        }
    }

    /// <summary>
    /// The host and the participant see the same code, made with the numbers of both sides.
    /// </summary>
    [Fact]
    public async Task VerificationCode_IsTheSameOnBothSides()
    {
        var token = TestContext.Current.CancellationToken;
        await using var guest = CreateGuest();

        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        var asked = (AdmissionRequest)_prompt.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IAdmissionPrompt.AskAsync)).GetArguments()[0]!;
        Assert.Equal(asked.VerificationCode.ToString(), guest.GetSnapshot().VerificationCode?.ToString());
    }

    /// <summary>
    /// A participant that reveals another number than the one it committed to (a man in the middle who would choose
    /// it after seeing the host's) is not let in, and the host user is not even asked.
    /// </summary>
    [Fact]
    public async Task Host_RefusesANumberThatDoesNotMatchItsCommitment()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        var connector = new TlsPeerConnector(_guestIdentity, _transport);
        await using var stream = await connector.ConnectAsync(new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.Control, token);
        await using var channel = new SessionMessageChannel(stream.Stream);

        await channel.SendAsync(new HelloMessage(SessionMessage.PROTOCOL_VERSION, "Марина", invite.Token) { SasCommit = SasCommitment.Commit(SasCommitment.CreateNonce()) }, token);
        Assert.IsType<PendingApprovalMessage>(await channel.ReceiveAsync(token));
        await channel.SendAsync(new SasRevealMessage(SasCommitment.CreateNonce()), token);

        Assert.Null(await channel.ReceiveAsync(token));
        await _prompt.DidNotReceive().AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A participant of the first version, which has no numbers for the code, is told that its version does not fit
    /// instead of being given a code an outsider could search for.
    /// </summary>
    [Fact]
    public async Task Host_RefusesAHelloWithoutCommitment()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        var connector = new TlsPeerConnector(_guestIdentity, _transport);
        await using var stream = await connector.ConnectAsync(new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.Control, token);
        await using var channel = new SessionMessageChannel(stream.Stream);

        await channel.SendAsync(new HelloMessage(1, "Марина", invite.Token), token);

        var refusal = Assert.IsType<RejectedMessage>(await channel.ReceiveAsync(token));
        Assert.Equal(RejectReason.IncompatibleVersion, refusal.Reason);
    }

    /// <summary>
    /// A participant does not take part in an entry that skips the numbers of the code: a host (or somebody posing
    /// as one) that names no number, or admits without asking, leaves nothing to compare.
    /// </summary>
    /// <param name="skipPending">Whether the host sends the welcome at once instead of a "pending" without a number.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Participant_RefusesAnEntryWithoutTheNumbers(bool skipPending)
    {
        var token = TestContext.Current.CancellationToken;
        using var hostIdentity = DeviceIdentity.CreateNew(TimeProvider.System);
        await using var listener = new TlsPeerListener(hostIdentity, _transport);
        listener.Start();
        var invite = new Invite
        {
            HostPeerId = hostIdentity.PeerId,
            HostName = "Хост",
            SessionName = "Вечер",
            Endpoints = [new PeerEndpoint("127.0.0.1", listener.Port)],
            Token = Invite.CreateToken(),
            ExpiresAt = TimeProvider.System.GetUtcNow().AddMinutes(5),
        };
        var playing = Task.Run(
            async () =>
            {
                await using var stream = await listener.AcceptAsync(token);
                await using var channel = new SessionMessageChannel(stream.Stream);
                Assert.IsType<HelloMessage>(await channel.ReceiveAsync(token));
                SessionMessage answer = skipPending
                    ? new WelcomeMessage("Вечер", [], PlaybackState.Initial(hostIdentity.PeerId, 0), null, 1)
                    : new PendingApprovalMessage();
                await channel.SendAsync(answer, token);
            },
            token);
        await using var guest = CreateGuest();

        var refusal = await Assert.ThrowsAsync<SessionJoinException>(() => guest.JoinAsync(invite, token));

        Assert.Equal(SessionTexts.DescribeRejection(RejectReason.IncompatibleVersion), refusal.Message);
        await playing.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    /// <summary>
    /// Creates a participant session of another device, without a player of its own.
    /// </summary>
    /// <param name="identity">The device.</param>
    /// <param name="name">The name of the participant.</param>
    /// <returns>The session.</returns>
    private static ParticipantSession CreateGuest(DeviceIdentity identity, string name)
        => new(
            identity,
            new TlsPeerConnector(identity, _transport),
            new SimulatedPlayer(SystemMonotonicClock.Instance, TimeSpan.FromHours(2)),
            new CapturingRegistry(),
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            name,
            _options);

    /// <summary>
    /// Creates a reconnector that does not wait between its attempts.
    /// </summary>
    /// <param name="client">The application.</param>
    /// <returns>The reconnector.</returns>
    private static ParticipantReconnector CreateReconnector(IReconnectionClient client)
        => new(client, new NoWaitClock(), NullLogger<ParticipantReconnector>.Instance);

    /// <summary>
    /// Joins the host and loses the connection.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session that ended and the ticket to come back with.</returns>
    private async Task<(ParticipantSession Ended, Invite Ticket)> JoinAndLoseAsync(CancellationToken cancellationToken)
    {
        var session = CreateGuest();
        await session.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), cancellationToken);
        var ticket = session.ReconnectInvite;
        Assert.NotNull(ticket);

        await session.DisposeAsync();
        await WaitUntilAsync(() => _host.GetSnapshot().Participants.Count == 1, cancellationToken);

        return (session, ticket);
    }

    /// <summary>
    /// Creates a participant session whose connection attempts fail at once.
    /// </summary>
    /// <returns>The session.</returns>
    private ParticipantSession CreateUnreachableGuest()
    {
        var connector = Substitute.For<IPeerConnector>();
        connector.ConnectAsync(Arg.Any<PeerEndpoint>(), Arg.Any<PeerId>(), Arg.Any<StreamPurpose>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PeerStream>(new IOException("The host is unreachable.")));

        return new ParticipantSession(
            _guestIdentity,
            connector,
            _guestPlayer,
            _registry,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Марина",
            _options,
            conference: _guestConference);
    }

    /// <summary>
    /// Creates a participant session.
    /// </summary>
    /// <returns>The session.</returns>
    private ParticipantSession CreateGuest()
        => new(
            _guestIdentity,
            new TlsPeerConnector(_guestIdentity, _transport),
            _guestPlayer,
            _registry,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Марина",
            _options,
            conference: _guestConference);

    /// <summary>
    /// When no address of the invitation answers, the participant meets the host through the rendezvous server of the
    /// invitation, and the media streams take the same route. The cameras ask the STUN port of that server.
    /// </summary>
    [Fact]
    public async Task Participant_MeetsHostThroughRendezvousWhenAddressesFail()
    {
        var token = TestContext.Current.CancellationToken;
        await _host.ShareMediaAsync(_mediaPath, token);
        var point = new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), RendezvousPoint.CreateRoom());
        _host.Rendezvous = point;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", ClosedPort())]);
        Assert.Equal(point, invite.Rendezvous);
        var rendezvous = new LoopbackRendezvous(_guestIdentity, _host.Port);
        await using var guest = CreateGuest(rendezvous);

        await guest.JoinAsync(invite, token);

        Assert.Equal(SessionState.Active, guest.GetSnapshot().State);
        Assert.Equal(point.StunUri, _guestConference.StunServer);
        using (var stream = _registry.Open())
        {
            var copy = new MemoryStream();
            await stream.CopyToAsync(copy, token);
            Assert.Equal(_content, copy.ToArray());
        }

        Assert.Contains(StreamPurpose.Control, rendezvous.Purposes);
        Assert.Contains(StreamPurpose.MediaData, rendezvous.Purposes);
        Assert.All(rendezvous.Points, p => Assert.Equal(point, p));
    }

    /// <summary>
    /// When the host shows another film, the participant goes on over the media streams it already has. Opening a
    /// stream through a rendezvous server is a whole meeting and can take many seconds, so the next film must not
    /// start by opening its own.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NextFilm_UsesTheMediaStreamsOfThePreviousOne()
    {
        var token = TestContext.Current.CancellationToken;
        await _host.ShareMediaAsync(_mediaPath, token);
        _host.Rendezvous = new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), RendezvousPoint.CreateRoom());
        var rendezvous = new LoopbackRendezvous(_guestIdentity, _host.Port);
        await using var guest = CreateGuest(rendezvous);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", ClosedPort())]), token);
        Assert.Equal(_content, await ReadAllAsync(_registry, token));
        var opened = rendezvous.Purposes.Count(purpose => purpose == StreamPurpose.MediaData);
        Assert.InRange(opened, 1, _options.MediaDataStreams);

        var second = Path.Combine(Path.GetTempPath(), $"golether-session-{Guid.NewGuid():N}.mkv");
        var other = RandomNumberGenerator.GetBytes(700_000);
        await File.WriteAllBytesAsync(second, other, token);
        try
        {
            await _host.ShareMediaAsync(second, token);
            await WaitUntilAsync(() => guest.GetSnapshot().Media?.Length == other.Length, token);

            Assert.Equal(other, await ReadAllAsync(_registry, token));
            Assert.Equal(opened, rendezvous.Purposes.Count(purpose => purpose == StreamPurpose.MediaData));
        }
        finally
        {
            await _host.ShareMediaAsync(_mediaPath, token);
            File.Delete(second);
        }
    }

    /// <summary>
    /// When the host serves relay streams, the cameras and voices of a participant reach its relay by the route of
    /// the session: they are pointed at a local port, and what is sent there arrives at the relay of the host over
    /// a stream of its own. No other port of the host has to be reachable.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Conference_ReachesRelayByTheSessionRoute()
    {
        var token = TestContext.Current.CancellationToken;
        var relay = new EchoRelay();
        _host.Relay = new Transports.Relay.RelayCredentials(47999, "golether-x", "secret_1");
        _host.RelayServer = relay;
        _host.Rendezvous = new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), RendezvousPoint.CreateRoom());
        var rendezvous = new LoopbackRendezvous(_guestIdentity, _host.Port);
        await using var guest = CreateGuest(rendezvous);

        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", ClosedPort())]), token);

        var match = System.Text.RegularExpressions.Regex.Match(_guestConference.Relay ?? string.Empty, @"^turn://golether-x:secret_1@127\.0\.0\.1:(\d+)\?transport=tcp$");
        Assert.True(match.Success, $"The cameras are pointed at a local port ({_guestConference.Relay}).");
        var port = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotEqual(47999, port);

        using var camera = new System.Net.Sockets.TcpClient();
        await camera.ConnectAsync(System.Net.IPAddress.Loopback, port, token);
        await camera.GetStream().WriteAsync(new byte[] { 1, 2, 3 }, token);
        var echoed = new byte[3];
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            await camera.GetStream().ReadExactlyAsync(echoed, limit.Token);
        }

        Assert.Equal([1, 2, 3], echoed);
        Assert.Contains(StreamPurpose.RelayShared, rendezvous.Purposes);
        Assert.Equal(System.Net.IPAddress.Loopback, Assert.Single(relay.Served).Local);
    }

    /// <summary>
    /// The cameras and voices make many connections to the relay at once. They all travel in one stream of the
    /// session: one meeting through the rendezvous server instead of one per connection, and the relay still
    /// serves each connection as a client of its own.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Conference_SharesOneStreamForAllRelayConnections()
    {
        var token = TestContext.Current.CancellationToken;
        var relay = new EchoRelay();
        _host.Relay = new Transports.Relay.RelayCredentials(47999, "golether-x", "secret_1");
        _host.RelayServer = relay;
        _host.Rendezvous = new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), RendezvousPoint.CreateRoom());
        var rendezvous = new LoopbackRendezvous(_guestIdentity, _host.Port);
        await using var guest = CreateGuest(rendezvous);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", ClosedPort())]), token);
        var port = new Uri(_guestConference.Relay!).Port;

        const int CONNECTIONS = 12;
        var cameras = new List<System.Net.Sockets.TcpClient>();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, CONNECTIONS).Select(async index =>
            {
                var camera = new System.Net.Sockets.TcpClient();
                lock (cameras)
                {
                    cameras.Add(camera);
                }

                await camera.ConnectAsync(System.Net.IPAddress.Loopback, port, token);
                await camera.GetStream().WriteAsync(new[] { (byte)index, (byte)(index + 1) }, token);
                var echoed = new byte[2];
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
                limit.CancelAfter(TimeSpan.FromSeconds(15));
                await camera.GetStream().ReadExactlyAsync(echoed, limit.Token);
                Assert.Equal([(byte)index, (byte)(index + 1)], echoed);
            }));

            Assert.Equal(CONNECTIONS, relay.Served.Count);
            Assert.Single(rendezvous.Purposes, purpose => purpose == StreamPurpose.RelayShared);
            Assert.DoesNotContain(StreamPurpose.Relay, rendezvous.Purposes);
        }
        finally
        {
            foreach (var camera in cameras)
            {
                camera.Dispose();
            }
        }
    }

    /// <summary>
    /// When the host ends the session (or closes the application, which ends it), every participant is told so:
    /// the session ends for it with that reason and is not restored.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task HostEndingTheSession_IsToldToParticipants()
    {
        var token = TestContext.Current.CancellationToken;
        await using var guest = CreateGuest();
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        await _host.DisposeAsync();

        await WaitUntilAsync(() => guest.GetSnapshot().State == SessionState.Ended, token);
        Assert.True(guest.ClosedByHost);
        Assert.Equal(Golether.Localization.Texts.Get("Session.Error.HostEnded"), guest.EndReason);
    }

    /// <summary>
    /// A session that has ended opens no more streams to the host. The cameras and the player do not know that the
    /// session is over and keep asking; left alone, every request would become one more meeting through the
    /// rendezvous server while the device is already trying to come back.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task EndedSession_OpensNoMoreStreams()
    {
        var token = TestContext.Current.CancellationToken;
        _host.Relay = new Transports.Relay.RelayCredentials(47999, "golether-x", "secret_1");
        _host.RelayServer = new EchoRelay();
        _host.Rendezvous = new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), RendezvousPoint.CreateRoom());
        var rendezvous = new LoopbackRendezvous(_guestIdentity, _host.Port);
        await using var guest = CreateGuest(rendezvous);
        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", ClosedPort())]), token);
        var port = new Uri(_guestConference.Relay!).Port;

        await _host.RemoveParticipantAsync(PeerIdOf(_guestIdentity));
        await WaitUntilAsync(() => guest.GetSnapshot().State == SessionState.Ended, token);
        var asked = rendezvous.Purposes.Count;

        using var camera = new System.Net.Sockets.TcpClient();
        await camera.ConnectAsync(System.Net.IPAddress.Loopback, port, token);
        Assert.Equal(0, await ReadUntilClosedAsync(camera.GetStream(), token).WaitAsync(TimeSpan.FromSeconds(10), token));
        Assert.Equal(asked, rendezvous.Purposes.Count);
    }

    /// <summary>
    /// A device with a valid TLS key but without admission cannot reach the relay: the host closes its relay stream
    /// without handing it over.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RelayStream_OfStranger_IsClosed()
    {
        var token = TestContext.Current.CancellationToken;
        var relay = new EchoRelay();
        _host.Relay = new Transports.Relay.RelayCredentials(47999, "golether-x", "secret_1");
        _host.RelayServer = relay;
        using var stranger = DeviceIdentity.CreateNew(TimeProvider.System);
        var connector = new TlsPeerConnector(stranger, _transport);

        await using var stream = await connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.Relay, token);

        Assert.Equal(0, await ReadUntilClosedAsync(stream.Stream, token).WaitAsync(TimeSpan.FromSeconds(10), token));
        Assert.Empty(relay.Served);
    }

    /// <summary>
    /// A relay that sends back what it receives and remembers whom it served.
    /// </summary>
    private sealed class EchoRelay : Transports.Relay.IRelayStreamServer
    {
        /// <summary>
        /// Gets the addresses of the served clients.
        /// </summary>
        public ConcurrentBag<(System.Net.IPEndPoint Remote, System.Net.IPAddress Local)> Served { get; } = [];

        /// <inheritdoc />
        public async Task ServeAsync(Stream stream, System.Net.IPEndPoint remote, System.Net.IPAddress local, CancellationToken cancellationToken)
        {
            Served.Add((remote, local));
            var buffer = new byte[1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// When an address of the invitation answers, the rendezvous server is not bothered.
    /// </summary>
    [Fact]
    public async Task Participant_PrefersDirectAddresses()
    {
        var token = TestContext.Current.CancellationToken;
        _host.Rendezvous = new RendezvousPoint(PeerEndpoint.Parse("203.0.113.7:47900"), RendezvousPoint.CreateRoom());
        var rendezvous = new LoopbackRendezvous(_guestIdentity, _host.Port);
        await using var guest = CreateGuest(rendezvous);

        await guest.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        Assert.Equal(SessionState.Active, guest.GetSnapshot().State);
        Assert.Empty(rendezvous.Purposes);
    }

    /// <summary>
    /// Finds a port nobody listens on.
    /// </summary>
    /// <returns>The port.</returns>
    private static int ClosedPort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>
    /// Creates a participant session that may meet the host through a rendezvous server.
    /// </summary>
    /// <param name="rendezvous">Meets the host.</param>
    /// <returns>The session.</returns>
    private ParticipantSession CreateGuest(IRendezvousConnector rendezvous)
        => new(
            _guestIdentity,
            new TlsPeerConnector(_guestIdentity, _transport),
            _guestPlayer,
            _registry,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Марина",
            _options,
            conference: _guestConference,
            rendezvous: rendezvous);

    /// <summary>
    /// Polls a condition.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    /// <exception cref="TimeoutException">The condition did not hold within 15 seconds.</exception>
    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    /// <summary>
    /// A rendezvous connector that "meets" the host over loopback and records what was asked of it.
    /// </summary>
    /// <param name="identity">The participant device.</param>
    /// <param name="hostPort">The port of the host on loopback.</param>
    private sealed class LoopbackRendezvous(DeviceIdentity identity, int hostPort) : IRendezvousConnector
    {
        /// <summary>
        /// Runs the real TLS handshake with the pinned key.
        /// </summary>
        private readonly TlsPeerConnector _tls = new(identity, _transport);

        /// <summary>
        /// Gets the purposes of the met streams.
        /// </summary>
        public ConcurrentBag<StreamPurpose> Purposes { get; } = [];

        /// <summary>
        /// Gets the points asked for.
        /// </summary>
        public ConcurrentBag<RendezvousPoint> Points { get; } = [];

        /// <inheritdoc />
        public Task<PeerStream> ConnectAsync(RendezvousPoint point, Core.Identity.PeerId expectedPeer, StreamPurpose purpose, CancellationToken cancellationToken)
        {
            Purposes.Add(purpose);
            Points.Add(point);
            return _tls.ConnectAsync(new PeerEndpoint("127.0.0.1", hostPort), expectedPeer, purpose, cancellationToken);
        }
    }

    /// <summary>
    /// A clock that does not wait: the timers of the attempts fire at once.
    /// </summary>
    private sealed class NoWaitClock : TimeProvider
    {
        /// <inheritdoc />
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => System.CreateTimer(callback, state, TimeSpan.Zero, period);
    }

    /// <summary>
    /// A clock that never comes: a wait on it ends only by other means.
    /// </summary>
    private sealed class FrozenClock : TimeProvider
    {
        /// <inheritdoc />
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The application around a session being restored.
    /// </summary>
    /// <param name="create">Creates the sessions of the attempts.</param>
    /// <param name="ticket">The ticket, or <see langword="null"/> when the user has left.</param>
    /// <param name="adopts">Whether the user is still there when the session is back.</param>
    private sealed class ReconnectingClient(Func<ParticipantSession> create, Invite? ticket, bool adopts = true) : IReconnectionClient
    {
        /// <summary>
        /// Gets what the user was told.
        /// </summary>
        public List<string> Reports { get; } = [];

        /// <summary>
        /// Gets the sessions of the attempts.
        /// </summary>
        public List<ParticipantSession> Created { get; } = [];

        /// <summary>
        /// Gets the restored session.
        /// </summary>
        public ParticipantSession? Adopted { get; private set; }

        /// <inheritdoc />
        public Invite? GetTicket(ParticipantSession ended) => ticket;

        /// <inheritdoc />
        public ParticipantSession CreateSession()
        {
            var session = create();
            Created.Add(session);
            return session;
        }

        /// <inheritdoc />
        public Task<bool> AdoptAsync(ParticipantSession ended, ParticipantSession session, Invite ticket, CancellationToken cancellationToken)
        {
            Adopted = adopts ? session : null;
            return Task.FromResult(adopts);
        }

        /// <inheritdoc />
        public void Report(string text) => Reports.Add(text);
    }

    /// <summary>
    /// Remembers the latest registered stream factory.
    /// </summary>
    private sealed class CapturingRegistry : IMediaUriRegistry
    {
        /// <summary>
        /// The factories by URI.
        /// </summary>
        private readonly ConcurrentDictionary<Uri, Func<Stream>> _factories = new();

        /// <summary>
        /// The latest registered URI.
        /// </summary>
        private Uri? _latest;

        /// <summary>
        /// Opens the latest registered stream.
        /// </summary>
        /// <returns>The stream.</returns>
        public Stream Open() => _factories[_latest!]();

        /// <inheritdoc />
        public Uri Register(Func<Stream> factory)
        {
            var uri = new Uri($"golether://media/{Guid.NewGuid():N}");
            _factories[uri] = factory;
            _latest = uri;
            return uri;
        }

        /// <inheritdoc />
        public void Unregister(Uri uri) => _factories.TryRemove(uri, out _);
    }

    /// <summary>
    /// An invitation pasted on the device that made it is refused with an explanation, before any connection is
    /// tried.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Join_RefusesTheOwnInvitation()
    {
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        await using var self = new ParticipantSession(
            _hostIdentity,
            new TlsPeerConnector(_hostIdentity, _transport),
            _guestPlayer,
            _registry,
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            "Вы",
            _options);

        var error = await Assert.ThrowsAsync<SessionJoinException>(() => self.JoinAsync(invite, TestContext.Current.CancellationToken));

        Assert.Contains("на этом устройстве", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A status with numbers at the limits of their types does not stop the host: the session keeps broadcasting,
    /// and the numbers the others see are bounded.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Host_SurvivesAStatusWithImpossibleNumbers()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        var connector = new TlsPeerConnector(_guestIdentity, _transport);
        await using var stream = await connector.ConnectAsync(new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, StreamPurpose.Control, token);
        await using var channel = new SessionMessageChannel(stream.Stream);
        await EnterByHandAsync(channel, invite.Token, token);

        await _host.RequestAsync(new PlaybackRequest(PlaybackRequestKind.PlayNow, null), token);
        await channel.SendAsync(
            new StatusReportMessage(new ParticipantStatus
            {
                PeerId = _guestIdentity.PeerId,
                Position = TimeSpan.MinValue,
                Drift = TimeSpan.MinValue,
                CacheAhead = TimeSpan.MaxValue,
                IsBuffering = true,
                RoundTripMilliseconds = int.MinValue,
            }),
            token);

        // The host still evaluates the statuses and tells everybody: the hostile one arrives bounded.
        ParticipantStatus? seen = null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        while (seen is null)
        {
            if (await channel.ReceiveAsync(limit.Token) is ParticipantsMessage participants)
            {
                seen = participants.Statuses.FirstOrDefault(s => s.PeerId == _guestIdentity.PeerId);
            }
        }

        Assert.Equal(-ParticipantStatus.MaxSpan, seen.Drift);
        Assert.Equal(0, seen.RoundTripMilliseconds);
        var before = _host.GetSnapshot().Playback!.Version;
        await _host.RequestAsync(new PlaybackRequest(PlaybackRequestKind.Pause, TimeSpan.FromSeconds(3)), token);
        Assert.True(_host.GetSnapshot().Playback!.Version > before);
        while (await channel.ReceiveAsync(limit.Token) is not ParticipantsMessage)
        {
        }
    }
}
