using System.Collections.Concurrent;
using System.Security.Cryptography;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Time;
using Golether.Media.Conference;
using Golether.Media.Player.Simulation;
using Golether.Media.Streaming.Caching;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Sync.Protocol;
using Golether.Transports.Tls;
using NSubstitute;

namespace Golether.Session.Tests;

/// <summary>
/// End-to-end tests of a host and participants over loopback TLS with simulated players.
/// </summary>
public sealed class SessionIntegrationTests : IAsyncLifetime
{
    /// <summary>
    /// Transport options: a free port.
    /// </summary>
    private static readonly TlsTransportOptions Transport = new() { Port = 0, HandshakeTimeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Session options with a short start lead to keep the test fast.
    /// </summary>
    private static readonly SessionOptions Options = new()
    {
        Authority = new() { StartLead = TimeSpan.FromMilliseconds(500) },
        TickInterval = TimeSpan.FromMilliseconds(50),
        StatusInterval = TimeSpan.FromMilliseconds(200),
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
    private readonly SimulatedPlayer _hostPlayer = new(StopwatchMonotonicClock.Instance, TimeSpan.FromHours(2));

    /// <summary>
    /// The participant player.
    /// </summary>
    private readonly SimulatedPlayer _guestPlayer = new(StopwatchMonotonicClock.Instance, TimeSpan.FromHours(2));

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
        var listener = new TlsPeerListener(_hostIdentity, Transport);
        listener.Start();
        _host = new HostSession(
            _hostIdentity,
            listener,
            new InviteRegistry(TimeProvider.System),
            new AdmissionService(_prompt, new AdmissionOptions(), TimeProvider.System),
            contacts: null,
            _hostPlayer,
            StopwatchMonotonicClock.Instance,
            TimeProvider.System,
            "Вечер кино",
            "Вы",
            Options,
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
        await using var secondPlayer = new SimulatedPlayer(StopwatchMonotonicClock.Instance, TimeSpan.FromHours(2));
        _guestConference.DataPeersSource = () => [secondIdentity.PeerId];
        _guestConference.DataSink = (_, data) => secondConference.ReceiveData(_guestIdentity.PeerId, data);
        secondConference.DataPeersSource = () => [_guestIdentity.PeerId];
        secondConference.DataSink = (_, data) => _guestConference.ReceiveData(secondIdentity.PeerId, data);

        await using var first = CreateGuest();
        await first.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);
        Assert.Equal(_content, await ReadAllAsync(_registry, token));
        Assert.Equal(2, _host.ChunksServed);

        await using var second = new ParticipantSession(
            secondIdentity,
            new TlsPeerConnector(secondIdentity, Transport),
            secondPlayer,
            secondRegistry,
            StopwatchMonotonicClock.Instance,
            TimeProvider.System,
            "Олег",
            Options,
            conference: secondConference);
        await second.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token);

        // The first participant announces its chunks every two seconds.
        await Task.Delay(TimeSpan.FromSeconds(2.5), token);
        Assert.Equal(_content, await ReadAllAsync(secondRegistry, token));

        Assert.Equal(2, _host.ChunksServed);
        Assert.True(_host.HashesServed >= 2, $"The host answered {_host.HashesServed} hashes.");
        var local = second.GetSnapshot().Participants.Single(p => p.IsLocal).Status!;
        Assert.Equal(_content.Length, local.BytesFromPeers);
        await WaitUntilAsync(() => first.GetSnapshot().Participants.Single(p => p.IsLocal).Status!.BytesToPeers == _content.Length, token);
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
            Arg.Is<AdmissionRequest>(r => r.PeerId == _guestIdentity.PeerId && r.DisplayName == "Марина"
                                          && r.VerificationCode.ToString() == snapshot.VerificationCode!.ToString()),
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
    /// A used invitation cannot be used again and a rejected participant gets the reason.
    /// </summary>
    [Fact]
    public async Task Admission_RejectsReusedInviteAndDeclinedPeer()
    {
        var token = TestContext.Current.CancellationToken;
        var invite = _host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]);
        await using (var first = CreateGuest())
        {
            await first.JoinAsync(invite, token);
        }

        await using var second = CreateGuest();
        var reused = await Assert.ThrowsAsync<SessionJoinException>(() => second.JoinAsync(invite, token));
        Assert.Contains("недействительно", reused.Message, StringComparison.Ordinal);

        _prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(false);
        await using var declined = CreateGuest();
        var error = await Assert.ThrowsAsync<SessionJoinException>(() =>
            declined.JoinAsync(_host.CreateInvite([new PeerEndpoint("127.0.0.1", _host.Port)]), token));
        Assert.Contains("отклонил", error.Message, StringComparison.Ordinal);
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
        var connector = new TlsPeerConnector(stranger, Transport);

        await using var stream = await connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, Transports.StreamPurpose.MediaData, token);

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
        var connector = new TlsPeerConnector(_guestIdentity, Transport);
        await using var stream = await connector.ConnectAsync(
            new PeerEndpoint("127.0.0.1", _host.Port), _hostIdentity.PeerId, Transports.StreamPurpose.MediaData, token);
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
    /// Creates a participant session.
    /// </summary>
    /// <returns>The session.</returns>
    private ParticipantSession CreateGuest()
        => new(
            _guestIdentity,
            new TlsPeerConnector(_guestIdentity, Transport),
            _guestPlayer,
            _registry,
            StopwatchMonotonicClock.Instance,
            TimeProvider.System,
            "Марина",
            Options,
            conference: _guestConference);

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
}
