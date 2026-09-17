using System.Collections.Concurrent;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Core.Time;
using Golether.Media.Conference;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Protocol;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Sync.Engine;
using Golether.Sync.Protocol;
using Golether.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Session;

/// <summary>
/// The session of the host: admits participants, shares the media file, owns the authoritative playback state and
/// plays the media locally like every other participant.
/// </summary>
public sealed class HostSession : IAsyncDisposable
{
    /// <summary>
    /// The local device.
    /// </summary>
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// The listener; started by the caller.
    /// </summary>
    private readonly IPeerListener _listener;

    /// <summary>
    /// The invitation tokens.
    /// </summary>
    private readonly InviteRegistry _invites;

    /// <summary>
    /// The admission policy.
    /// </summary>
    private readonly AdmissionService _admission;

    /// <summary>
    /// The contacts, or <see langword="null"/>.
    /// </summary>
    private readonly IContactStore? _contacts;

    /// <summary>
    /// The local player.
    /// </summary>
    private readonly IPlaybackController _player;

    /// <summary>
    /// The session options.
    /// </summary>
    private readonly SessionOptions _options;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The session clock (the local monotonic clock).
    /// </summary>
    private readonly HostSessionClock _clock;

    /// <summary>
    /// The authoritative state.
    /// </summary>
    private readonly PlaybackAuthority _authority;

    /// <summary>
    /// The local follower.
    /// </summary>
    private readonly PlaybackFollower _follower;

    /// <summary>
    /// The shared file.
    /// </summary>
    private readonly LocalSharedMedia _media = new();

    /// <summary>
    /// Serves media data streams.
    /// </summary>
    private readonly ChunkServer _chunkServer;

    /// <summary>
    /// Admitted participants.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, Connection> _participants = new();

    /// <summary>
    /// Serializes player access.
    /// </summary>
    private readonly SemaphoreSlim _playerGate = new(1, 1);

    /// <summary>
    /// Stops the session.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Background tasks.
    /// </summary>
    private readonly List<Task> _loops = [];

    /// <summary>
    /// Cameras and voices, or <see langword="null"/>.
    /// </summary>
    private readonly ConferenceLink? _conference;

    /// <summary>
    /// The latest local follower status.
    /// </summary>
    private FollowerStatus? _localStatus;

    /// <summary>
    /// Whether the session has ended.
    /// </summary>
    private int _ended;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostSession"/> class.
    /// </summary>
    /// <param name="identity">The local device.</param>
    /// <param name="listener">The started listener; the session owns it.</param>
    /// <param name="invites">The invitation tokens.</param>
    /// <param name="admission">The admission policy.</param>
    /// <param name="contacts">The contacts, or <see langword="null"/>.</param>
    /// <param name="player">The local player.</param>
    /// <param name="clock">The local monotonic clock.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="sessionName">The session name.</param>
    /// <param name="displayName">The host name.</param>
    /// <param name="options">The options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="conference">Cameras and voices, or <see langword="null"/>.</param>
    public HostSession(
        DeviceIdentity identity,
        IPeerListener listener,
        InviteRegistry invites,
        AdmissionService admission,
        IContactStore? contacts,
        IPlaybackController player,
        IMonotonicClock clock,
        TimeProvider timeProvider,
        string sessionName,
        string displayName,
        SessionOptions options,
        ILoggerFactory? loggerFactory = null,
        IConferenceMedia? conference = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _contacts = contacts;
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<HostSession>();

        SessionName = ParticipantInfo.NormalizeDisplayName(sessionName);
        DisplayName = ParticipantInfo.NormalizeDisplayName(displayName);
        _clock = new HostSessionClock(clock ?? throw new ArgumentNullException(nameof(clock)));
        _authority = new PlaybackAuthority(identity.PeerId, _clock, options.Authority);
        _follower = new PlaybackFollower(player, _clock, new DriftCorrector(options.DriftCorrection), loggerFactory.CreateLogger<PlaybackFollower>());
        _chunkServer = new ChunkServer(_media, loggerFactory.CreateLogger<ChunkServer>());
        _conference = conference is null
            ? null
            : new ConferenceLink(conference, identity.PeerId, SendConferenceSignalAsync, loggerFactory.CreateLogger<ConferenceLink>());
    }

    /// <summary>
    /// Raised when the snapshot changed (participants, media, state). Raised on a background thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised for the event feed. Raised on a background thread.
    /// </summary>
    public event EventHandler<SessionEvent>? EventRaised;

    /// <summary>
    /// Gets the session name.
    /// </summary>
    public string SessionName { get; }

    /// <summary>
    /// Gets the host name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the listening port.
    /// </summary>
    public int Port => _listener.Port;

    /// <summary>
    /// Gets or sets the TURN relay announced to admitted participants, or <see langword="null"/>.
    /// </summary>
    public Transports.Relay.RelayCredentials? Relay { get; set; }

    /// <summary>
    /// Gets the number of file chunks the host uploaded.
    /// </summary>
    public long ChunksServed => _chunkServer.ChunksServed;

    /// <summary>
    /// Gets the number of chunk hashes the host answered (participants check shared chunks with them).
    /// </summary>
    public long HashesServed => _chunkServer.HashesServed;

    /// <summary>
    /// Gets the path of the shared file, or <see langword="null"/>.
    /// </summary>
    public string? SharedFilePath => _media.FilePath;

    /// <summary>
    /// Starts accepting participants and the synchronization loop.
    /// </summary>
    public void Start()
    {
        if (_loops.Count > 0)
        {
            return;
        }

        _follower.ApplyAsync(_authority.Current, CancellationToken.None).GetAwaiter().GetResult();
        _loops.Add(Task.Run(() => AcceptLoopAsync(_stopping.Token)));
        _loops.Add(Task.Run(() => TickLoopAsync(_stopping.Token)));
        _conference?.Start();
        Raise(null, $"Сеанс «{SessionName}» создан. Порт {Port}.");
    }

    /// <summary>
    /// Creates an invitation.
    /// </summary>
    /// <param name="endpoints">The addresses to put into the invitation.</param>
    /// <param name="lifetime">The lifetime; the default when <see langword="null"/>.</param>
    /// <returns>The invitation.</returns>
    public Invite CreateInvite(IReadOnlyList<PeerEndpoint> endpoints, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (endpoints.Count == 0)
        {
            throw new ArgumentException("An invitation needs at least one address.", nameof(endpoints));
        }

        var (token, expires) = _invites.Issue(lifetime);
        return new Invite
        {
            HostPeerId = _identity.PeerId,
            HostName = DisplayName,
            SessionName = SessionName,
            Endpoints = endpoints.Take(Invite.MaxEndpoints).ToArray(),
            Token = token,
            ExpiresAt = expires,
        };
    }

    /// <summary>
    /// Shares a local file and loads it into the local player.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor.</returns>
    public async Task<MediaDescriptor> ShareMediaAsync(string path, CancellationToken cancellationToken)
    {
        var descriptor = await MediaFiles.DescribeAsync(path, cancellationToken).ConfigureAwait(false);
        _media.Share(path, descriptor);
        var state = _authority.Reset();
        _authority.Duration = null;

        await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _follower.Reset();
            await _player.LoadAsync(new Uri(Path.GetFullPath(path)), TimeSpan.Zero, paused: true, cancellationToken).ConfigureAwait(false);
            await _follower.ApplyAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _playerGate.Release();
        }

        await BroadcastAsync(new MediaChangedMessage(descriptor)).ConfigureAwait(false);
        await BroadcastAsync(new PlaybackStateMessage(state)).ConfigureAwait(false);
        Raise(_identity.PeerId, $"{DisplayName} показывает «{descriptor.FileName}»");
        return descriptor;
    }

    /// <summary>
    /// Applies a playback intent of the host user.
    /// </summary>
    /// <param name="request">The intent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the state was broadcast.</returns>
    public async Task RequestAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _follower.ApplyLocalIntentAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _playerGate.Release();
        }

        var state = _authority.Apply(_identity.PeerId, request, MaxRoundTrip());
        if (state is not null)
        {
            await PublishAsync(state, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Switches devices of a participant off. The host can only switch off; the participant may switch them on again.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request was sent.</returns>
    public async Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
    {
        if (!(microphone || camera) || peerId == _identity.PeerId || !_participants.TryGetValue(peerId, out var connection))
        {
            return;
        }

        await TrySendAsync(connection, new ModerationMessage(microphone, camera)).ConfigureAwait(false);
        if (connection.Status is { } status)
        {
            connection.Status = status with { MicrophoneOff = status.MicrophoneOff || microphone, CameraOff = status.CameraOff || camera };
        }

        var what = (microphone, camera) switch
        {
            (true, true) => "микрофон и камеру",
            (true, false) => "микрофон",
            _ => "камеру",
        };
        Raise(_identity.PeerId, $"Ведущий выключает {what} участника {connection.Info.DisplayName}");
    }

    /// <summary>
    /// Removes a participant.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <returns>A task that completes when the participant was notified.</returns>
    public async Task RemoveParticipantAsync(PeerId peerId)
    {
        if (_participants.TryRemove(peerId, out var connection))
        {
            connection.End();
            await TrySendAsync(connection, new RejectedMessage(RejectReason.Removed, "Ведущий завершил ваше участие.")).ConfigureAwait(false);
            await connection.Channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the current view of the session.
    /// </summary>
    /// <returns>The snapshot.</returns>
    public SessionSnapshot GetSnapshot()
    {
        var local = new ParticipantView(new ParticipantInfo(_identity.PeerId, DisplayName, true), BuildLocalStatus(), true);
        var others = _participants.Values.OrderBy(c => c.JoinedAt).Select(c => new ParticipantView(c.Info, c.Status, false));
        return new SessionSnapshot
        {
            IsHost = true,
            State = _ended != 0 ? SessionState.Ended : SessionState.Active,
            SessionName = SessionName,
            HostPeerId = _identity.PeerId,
            Participants = [local, .. others],
            Playback = _authority.Current,
            Media = _media.Descriptor,
            Local = _localStatus,
        };
    }

    /// <summary>
    /// Ends the session for everybody.
    /// </summary>
    /// <returns>A task that completes when all connections are closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return;
        }

        await BroadcastAsync(new ByeMessage("Ведущий завершил сеанс.")).ConfigureAwait(false);
        await _stopping.CancelAsync().ConfigureAwait(false);
        foreach (var connection in _participants.Values)
        {
            connection.End();
            await connection.Channel.DisposeAsync().ConfigureAwait(false);
        }

        _participants.Clear();
        await _listener.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(_loops.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default))).ConfigureAwait(false);
        await _player.StopAsync().ConfigureAwait(false);
        if (_conference is not null)
        {
            await _conference.DisposeAsync().ConfigureAwait(false);
        }
        _stopping.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Accepts streams.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the session ends.</returns>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PeerStream stream;
            try
            {
                stream = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.Threading.Channels.ChannelClosedException)
            {
                return;
            }

            _ = stream.Purpose switch
            {
                StreamPurpose.Control => Task.Run(() => HandleControlAsync(stream, cancellationToken), CancellationToken.None),
                _ => Task.Run(() => HandleMediaAsync(stream, cancellationToken), CancellationToken.None),
            };
        }
    }

    /// <summary>
    /// Serves a media data stream of an admitted participant.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>A task that completes when the stream ends.</returns>
    private async Task HandleMediaAsync(PeerStream stream, CancellationToken cancellationToken)
    {
        await using (stream.ConfigureAwait(false))
        {
            if (!_participants.TryGetValue(stream.RemotePeer, out var owner))
            {
                _logger.LogWarning("Media stream from {Peer} refused: not admitted", stream.RemotePeer.ToShortString());
                return;
            }

            // The stream lives only as long as the admission: leaving, removal or a new connection closes it.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Lifetime);
            using var closeOnEnd = linked.Token.Register(() => stream.Stream.Dispose());
            try
            {
                await _chunkServer.ServeAsync(stream.Stream, linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
            {
                _logger.LogDebug("Media stream of {Peer} ended: {Reason}", stream.RemotePeer.ToShortString(), ex.Message);
            }
        }
    }

    /// <summary>
    /// Admits a participant and processes its messages.
    /// </summary>
    /// <param name="stream">The control stream.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>A task that completes when the participant leaves.</returns>
    private async Task HandleControlAsync(PeerStream stream, CancellationToken cancellationToken)
    {
        var peer = stream.RemotePeer;
        var channel = new SessionMessageChannel(stream.Stream);
        Connection? connection = null;
        try
        {
            HelloMessage? hello;
            using (var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                helloTimeout.CancelAfter(_options.HelloTimeout);
                hello = await channel.ReceiveAsync(helloTimeout.Token).ConfigureAwait(false) as HelloMessage;
            }

            connection = await AdmitAsync(peer, hello, stream.RemoteAddress, channel, cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (message is null or ByeMessage)
                {
                    return;
                }

                await HandleMessageAsync(connection, message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogInformation("Control stream of {Peer} ended: {Reason}", peer.ToShortString(), ex.Message);
        }
        finally
        {
            connection?.End();
            if (connection is not null && _participants.TryRemove(new KeyValuePair<PeerId, Connection>(peer, connection)))
            {
                Raise(peer, $"{connection.Info.DisplayName} покидает сеанс");
                await SyncConferenceAsync().ConfigureAwait(false);
                await BroadcastParticipantsAsync().ConfigureAwait(false);
            }

            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the admission of a peer.
    /// </summary>
    /// <param name="peer">The authenticated peer.</param>
    /// <param name="hello">The hello, or <see langword="null"/> when the peer sent something else.</param>
    /// <param name="remoteAddress">The remote address.</param>
    /// <param name="channel">The control channel.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>The admitted connection, or <see langword="null"/>.</returns>
    private async Task<Connection?> AdmitAsync(PeerId peer, HelloMessage? hello, string? remoteAddress, SessionMessageChannel channel, CancellationToken cancellationToken)
    {
        if (hello is null)
        {
            return null;
        }

        if (hello.Version != SessionMessage.ProtocolVersion)
        {
            await channel.SendAsync(new RejectedMessage(RejectReason.IncompatibleVersion, "Версии Golether несовместимы. Обновите приложение."), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (_participants.Count + 1 >= _options.MaxParticipants && !_participants.ContainsKey(peer))
        {
            await channel.SendAsync(new RejectedMessage(RejectReason.SessionFull, $"В сеансе уже {_options.MaxParticipants} участников."), cancellationToken).ConfigureAwait(false);
            return null;
        }

        var contact = _contacts is null ? null : await _contacts.FindAsync(peer, cancellationToken).ConfigureAwait(false);
        var trusted = contact?.IsTrusted == true;
        if (!trusted)
        {
            var check = _invites.TryConsume(hello.InviteToken);
            if (check != InviteCheckResult.Accepted)
            {
                var text = check == InviteCheckResult.Expired ? "Срок действия приглашения истёк." : "Приглашение недействительно или уже использовано.";
                await channel.SendAsync(new RejectedMessage(RejectReason.InvalidInvite, text), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Rejected {Peer}: invite {Result}", peer.ToShortString(), check);
                return null;
            }
        }

        var name = ParticipantInfo.NormalizeDisplayName(hello.DisplayName);
        var code = VerificationCode.Compute(_identity.PeerId, peer, hello.InviteToken ?? string.Empty);
        await channel.SendAsync(new PendingApprovalMessage(), cancellationToken).ConfigureAwait(false);
        var decision = await _admission.DecideAsync(new AdmissionRequest(peer, name, code, trusted), cancellationToken).ConfigureAwait(false);
        if (decision != AdmissionDecision.Approved)
        {
            var reason = decision == AdmissionDecision.TimedOut ? RejectReason.ApprovalTimedOut : RejectReason.Declined;
            await channel.SendAsync(new RejectedMessage(reason, decision == AdmissionDecision.TimedOut ? "Ведущий не ответил." : "Ведущий отклонил запрос."), cancellationToken).ConfigureAwait(false);
            Raise(peer, $"{name}: запрос на подключение отклонён");
            return null;
        }

        if (_contacts is not null)
        {
            await _contacts.TouchAsync(peer, name, remoteAddress, cancellationToken).ConfigureAwait(false);
        }

        var connection = new Connection(new ParticipantInfo(peer, name, false), channel, _timeProvider.GetUtcNow());
        if (_participants.TryGetValue(peer, out var previous))
        {
            previous.End();
            await previous.Channel.DisposeAsync().ConfigureAwait(false);
        }

        _participants[peer] = connection;
        var participants = GetSnapshot().Participants.Select(p => p.Info).ToArray();
        await channel.SendAsync(
            new WelcomeMessage(SessionName, participants, _authority.Current, _media.Descriptor, _options.MediaDataStreams) { Relay = Relay },
            cancellationToken).ConfigureAwait(false);
        Raise(peer, $"{name} присоединяется");
        _ = SyncConferenceAsync();
        await BroadcastParticipantsAsync().ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Processes a message of an admitted participant.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>A task that completes when the message is processed.</returns>
    private async Task HandleMessageAsync(Connection connection, SessionMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ClockPingMessage ping:
                var received = _clock.NowMicroseconds;
                await connection.Channel.SendAsync(new ClockPongMessage(ping.ClientSendTime, received, _clock.NowMicroseconds), cancellationToken).ConfigureAwait(false);
                break;

            case PlaybackRequestMessage request:
                PlaybackState? state;
                try
                {
                    state = _authority.Apply(connection.Info.PeerId, request.Request, MaxRoundTrip());
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning("Invalid request of {Peer}: {Reason}", connection.Info.PeerId.ToShortString(), ex.Message);
                    break;
                }

                if (state is not null)
                {
                    await PublishAsync(state, cancellationToken).ConfigureAwait(false);
                }

                break;

            case ConferenceSignalMessage signal when signal.Kind.Length <= 16 && signal.Payload.Length <= 64 * 1024:
                if (signal.Peer == _identity.PeerId)
                {
                    await (_conference?.ReceiveAsync(connection.Info.PeerId, signal.Kind, signal.Payload) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                else if (_participants.TryGetValue(signal.Peer, out var target))
                {
                    // Relay between participants; the source is the authenticated sender, never taken from the message.
                    await TrySendAsync(target, signal with { Peer = connection.Info.PeerId }).ConfigureAwait(false);
                }

                break;

            case StatusReportMessage report:
                connection.Status = report.Status with { PeerId = connection.Info.PeerId };
                break;

            default:
                _logger.LogDebug("Ignored {Message} from {Peer}", message.GetType().Name, connection.Info.PeerId.ToShortString());
                break;
        }
    }

    /// <summary>
    /// Applies a new state locally and broadcasts it.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the state was sent.</returns>
    private async Task PublishAsync(PlaybackState state, CancellationToken cancellationToken)
    {
        await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _follower.ApplyAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _playerGate.Release();
        }

        await BroadcastAsync(new PlaybackStateMessage(state)).ConfigureAwait(false);
        Raise(state.Origin, SessionTexts.Describe(state, NameOf(state.Origin)));
    }

    /// <summary>
    /// Sends a signal of the local conference to a participant.
    /// </summary>
    /// <param name="signal">The signal addressed to the participant.</param>
    /// <returns>A task that completes when the signal was sent.</returns>
    private Task SendConferenceSignalAsync(ConferenceSignal signal)
        => _participants.TryGetValue(signal.PeerId, out var target)
            ? TrySendAsync(target, new ConferenceSignalMessage(_identity.PeerId, signal.Kind, signal.Payload))
            : Task.CompletedTask;

    /// <summary>
    /// Connects the cameras of the admitted participants.
    /// </summary>
    /// <returns>A task that completes when the set is updated.</returns>
    private Task SyncConferenceAsync()
        => _conference?.SyncAsync(_participants.Keys.ToArray()) ?? Task.CompletedTask;

    /// <summary>
    /// Runs the synchronization loop.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the session ends.</returns>
    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.TickInterval, _timeProvider);
        var lastBroadcast = _timeProvider.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    _localStatus = await _follower.TickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Player tick failed");
                }
                finally
                {
                    _playerGate.Release();
                }

                if (_localStatus?.Snapshot.Duration is { } duration)
                {
                    _authority.Duration = duration;
                }

                if (_timeProvider.GetElapsedTime(lastBroadcast) < _options.StatusInterval)
                {
                    continue;
                }

                lastBroadcast = _timeProvider.GetTimestamp();
                var statuses = _participants.Values.Select(c => c.Status).OfType<ParticipantStatus>().ToArray();
                var hold = _authority.EvaluateBuffering(statuses, MaxRoundTrip());
                if (hold is not null)
                {
                    await PublishAsync(hold, cancellationToken).ConfigureAwait(false);
                }

                await BroadcastParticipantsAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Builds the status of the host itself.
    /// </summary>
    /// <returns>The status.</returns>
    private ParticipantStatus BuildLocalStatus()
    {
        var local = _localStatus;
        return new ParticipantStatus
        {
            PeerId = _identity.PeerId,
            Position = local?.Snapshot.Position,
            Drift = local?.Drift ?? TimeSpan.Zero,
            CacheAhead = local?.Snapshot.CacheAhead ?? TimeSpan.Zero,
            IsBuffering = local?.Snapshot.IsBuffering ?? false,
            UsesLocalCopy = true,
            MicrophoneOff = _conference?.MicrophoneOff ?? true,
            CameraOff = _conference?.CameraOff ?? true,
        };
    }

    /// <summary>
    /// Returns the largest round trip reported by the participants.
    /// </summary>
    /// <returns>The round trip.</returns>
    private TimeSpan MaxRoundTrip()
        => TimeSpan.FromMilliseconds(_participants.Values.Select(c => c.Status?.RoundTripMilliseconds ?? 0).DefaultIfEmpty(0).Max());

    /// <summary>
    /// Returns the name of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <returns>The name.</returns>
    private string NameOf(PeerId peer)
        => peer == _identity.PeerId ? DisplayName
            : _participants.TryGetValue(peer, out var connection) ? connection.Info.DisplayName
            : "Участник";

    /// <summary>
    /// Sends the participant list to everybody.
    /// </summary>
    /// <returns>A task that completes when the message was sent.</returns>
    private async Task BroadcastParticipantsAsync()
    {
        var snapshot = GetSnapshot();
        await BroadcastAsync(new ParticipantsMessage(
            snapshot.Participants.Select(p => p.Info).ToArray(),
            snapshot.Participants.Select(p => p.Status).OfType<ParticipantStatus>().ToArray())).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sends a message to all participants; participants that fail are disconnected.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when all sends finished.</returns>
    private Task BroadcastAsync(SessionMessage message)
        => Task.WhenAll(_participants.Values.Select(c => TrySendAsync(c, message)));

    /// <summary>
    /// Sends a message with a timeout and disconnects the participant on failure.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the send finished.</returns>
    private async Task TrySendAsync(Connection connection, SessionMessage message)
    {
        using var timeout = new CancellationTokenSource(_options.SendTimeout);
        try
        {
            await connection.Channel.SendAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogInformation("Dropping {Peer}: {Reason}", connection.Info.PeerId.ToShortString(), ex.Message);
            await connection.Channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Raises an event feed entry and a change notification.
    /// </summary>
    /// <param name="actor">The participant, or <see langword="null"/>.</param>
    /// <param name="text">The text.</param>
    private void Raise(PeerId? actor, string text)
    {
        EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), actor, text));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// An admitted participant.
    /// </summary>
    private sealed class Connection
    {
        /// <summary>
        /// Cancelled when the admission ends.
        /// </summary>
        private readonly CancellationTokenSource _lifetime = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Connection"/> class.
        /// </summary>
        /// <param name="info">The participant.</param>
        /// <param name="channel">The control channel.</param>
        /// <param name="joinedAt">The admission time.</param>
        public Connection(ParticipantInfo info, SessionMessageChannel channel, DateTimeOffset joinedAt)
        {
            Info = info;
            Channel = channel;
            JoinedAt = joinedAt;
        }

        /// <summary>
        /// Gets the participant.
        /// </summary>
        public ParticipantInfo Info { get; }

        /// <summary>
        /// Gets the control channel.
        /// </summary>
        public SessionMessageChannel Channel { get; }

        /// <summary>
        /// Gets the admission time.
        /// </summary>
        public DateTimeOffset JoinedAt { get; }

        /// <summary>
        /// Gets or sets the latest status.
        /// </summary>
        public ParticipantStatus? Status { get; set; }

        /// <summary>
        /// Gets a token that is cancelled when the admission ends.
        /// </summary>
        public CancellationToken Lifetime => _lifetime.Token;

        /// <summary>
        /// Ends the admission: closes the media streams of the participant.
        /// </summary>
        public void End() => _lifetime.Cancel();
    }
}
