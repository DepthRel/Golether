using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Core.Time;
using Golether.Media.Conference;
using Golether.Media.Streaming.Caching;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Protocol;
using Golether.Media.Streaming.Sharing;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Sync.Clock;
using Golether.Sync.Engine;
using Golether.Sync.Protocol;
using Golether.Transports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Golether.Session;

/// <summary>
/// The session of a participant: joins the host, synchronizes the clock, receives the media and follows the
/// authoritative playback state.
/// </summary>
public sealed class ParticipantSession : IAsyncDisposable
{
    /// <summary>
    /// The number of fast clock probes after joining.
    /// </summary>
    private const int InitialProbes = 6;

    /// <summary>
    /// The local device.
    /// </summary>
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// Opens streams to the host.
    /// </summary>
    private readonly IPeerConnector _connector;

    /// <summary>
    /// The local player.
    /// </summary>
    private readonly IPlaybackController _player;

    /// <summary>
    /// Publishes media streams to the player.
    /// </summary>
    private readonly IMediaUriRegistry _uriRegistry;

    /// <summary>
    /// The local monotonic clock.
    /// </summary>
    private readonly IMonotonicClock _monotonic;

    /// <summary>
    /// The clock offset estimator.
    /// </summary>
    private readonly ClockOffsetEstimator _estimator = new();

    /// <summary>
    /// The session clock.
    /// </summary>
    private readonly ParticipantSessionClock _clock;

    /// <summary>
    /// The follower.
    /// </summary>
    private readonly PlaybackFollower _follower;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly SessionOptions _options;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger factory.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

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
    /// Guards the mutable view fields.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The control channel.
    /// </summary>
    private SessionMessageChannel? _channel;

    /// <summary>
    /// The invitation used to join.
    /// </summary>
    private Invite? _invite;

    /// <summary>
    /// The ticket that lets this device come back to the session without a new invitation.
    /// </summary>
    private string? _reconnectTicket;

    /// <summary>
    /// Whether this is a return to a session this device was already admitted to.
    /// </summary>
    private bool _returning;

    /// <summary>
    /// The host endpoint that answered.
    /// </summary>
    private PeerEndpoint _hostEndpoint;

    /// <summary>
    /// The number of parallel media streams allowed by the host.
    /// </summary>
    private int _mediaStreams = 1;

    /// <summary>
    /// The connection state.
    /// </summary>
    private SessionState _state = SessionState.Connecting;

    /// <summary>
    /// The session name.
    /// </summary>
    private string _sessionName = string.Empty;

    /// <summary>
    /// The participants and statuses from the host.
    /// </summary>
    private IReadOnlyList<ParticipantView> _participants = [];

    /// <summary>
    /// The newest authoritative state received.
    /// </summary>
    private PlaybackState? _latestState;

    /// <summary>
    /// The shared media.
    /// </summary>
    private MediaDescriptor? _media;

    /// <summary>
    /// The reader of the remote media.
    /// </summary>
    private CachedMediaReader? _reader;

    /// <summary>
    /// Cameras and voices, also used to share file chunks, or <see langword="null"/>.
    /// </summary>
    private readonly IConferenceMedia? _conferenceMedia;

    /// <summary>
    /// The chunk sharing with other participants, or <see langword="null"/>.
    /// </summary>
    private ChunkSharing? _sharing;

    /// <summary>
    /// The source that takes chunks from participants, or <see langword="null"/>.
    /// </summary>
    private SwarmChunkSource? _swarm;

    /// <summary>
    /// The URI registered for the remote media.
    /// </summary>
    private Uri? _mediaUri;

    /// <summary>
    /// Whether a local copy is played.
    /// </summary>
    private bool _usesLocalCopy;

    /// <summary>
    /// The latest follower status.
    /// </summary>
    private FollowerStatus? _localStatus;

    /// <summary>
    /// The verification code.
    /// </summary>
    private VerificationCode? _code;

    /// <summary>
    /// Whether the session was disposed.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantSession"/> class.
    /// </summary>
    /// <param name="identity">The local device.</param>
    /// <param name="connector">Opens streams to the host.</param>
    /// <param name="player">The local player.</param>
    /// <param name="uriRegistry">Publishes media streams to the player.</param>
    /// <param name="clock">The local monotonic clock.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="displayName">The participant name.</param>
    /// <param name="options">The options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="conference">Cameras and voices, or <see langword="null"/>.</param>
    public ParticipantSession(
        DeviceIdentity identity,
        IPeerConnector connector,
        IPlaybackController player,
        IMediaUriRegistry uriRegistry,
        IMonotonicClock clock,
        TimeProvider timeProvider,
        string displayName,
        SessionOptions options,
        ILoggerFactory? loggerFactory = null,
        IConferenceMedia? conference = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _uriRegistry = uriRegistry ?? throw new ArgumentNullException(nameof(uriRegistry));
        _monotonic = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ParticipantSession>();
        DisplayName = ParticipantInfo.NormalizeDisplayName(displayName);
        _clock = new ParticipantSessionClock(clock, _estimator);
        _follower = new PlaybackFollower(player, _clock, new DriftCorrector(options.DriftCorrection), _loggerFactory.CreateLogger<PlaybackFollower>());
        _conferenceMedia = conference;
        _conference = conference is null
            ? null
            : new ConferenceLink(conference, identity.PeerId, SendConferenceSignalAsync, _loggerFactory.CreateLogger<ConferenceLink>());
    }

    /// <summary>
    /// Raised when the snapshot changed. Raised on a background thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised for the event feed. Raised on a background thread.
    /// </summary>
    public event EventHandler<SessionEvent>? EventRaised;

    /// <summary>
    /// Raised for chat lines and reactions relayed by the host, including this device's own. Raised on a background
    /// thread.
    /// </summary>
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <summary>
    /// Raised for the strokes drawn over the video, including this device's own (the host relays them back). Raised
    /// on a background thread.
    /// </summary>
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Gets the participant name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Comes back to a session this device was admitted to, using the ticket instead of an invitation. The host is
    /// asked again.
    /// </summary>
    /// <param name="ticket">The invitation carrying the ticket (see <see cref="ReconnectInvite"/>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the session is joined.</returns>
    public Task RejoinAsync(Invite ticket, CancellationToken cancellationToken)
    {
        _returning = true;
        return JoinAsync(ticket, cancellationToken);
    }

    /// <summary>
    /// Gets the invitation that brings this device back to the session, or <see langword="null"/> without a ticket.
    /// </summary>
    public Invite? ReconnectInvite
        => _invite is { } invite && _reconnectTicket is { Length: > 0 } ticket
            ? invite with { Token = ticket, ExpiresAt = _timeProvider.GetUtcNow().AddHours(12) }
            : null;

    /// <summary>
    /// Connects to the host and waits until the host admits or rejects this device.
    /// </summary>
    /// <param name="invite">The invitation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the session is active.</returns>
    /// <exception cref="SessionJoinException">The host cannot be reached or rejected the request.</exception>
    public async Task JoinAsync(Invite invite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invite);
        if (_invite is not null)
        {
            throw new InvalidOperationException("The session was already joined.");
        }

        if (!_returning && invite.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            throw new SessionJoinException("Срок действия приглашения истёк. Попросите ведущего прислать новое.");
        }

        _invite = invite;
        _sessionName = invite.SessionName;
        _code = VerificationCode.Compute(invite.HostPeerId, _identity.PeerId, invite.Token);
        var stream = await ConnectAnyAsync(invite, cancellationToken).ConfigureAwait(false);
        _channel = new SessionMessageChannel(stream.Stream);
        await _channel.SendAsync(new HelloMessage(SessionMessage.ProtocolVersion, DisplayName, invite.Token), cancellationToken).ConfigureAwait(false);

        WelcomeMessage welcome;
        while (true)
        {
            SessionMessage? message;
            try
            {
                message = await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                throw new SessionJoinException("Соединение с ведущим прервалось.", ex);
            }

            switch (message)
            {
                case PendingApprovalMessage:
                    SetState(SessionState.AwaitingApproval);
                    continue;
                case RejectedMessage rejected:
                    SetState(SessionState.Ended);
                    throw new SessionJoinException(rejected.Message);
                case WelcomeMessage accepted:
                    welcome = accepted;
                    break;
                case null:
                    SetState(SessionState.Ended);
                    throw new SessionJoinException("Ведущий закрыл соединение.");
                default:
                    continue;
            }

            break;
        }

        lock (_gate)
        {
            _sessionName = welcome.SessionName;
            _mediaStreams = Math.Clamp(welcome.MediaDataStreams, 1, 16);
            _participants = welcome.Participants.Select(p => new ParticipantView(p, null, p.PeerId == _identity.PeerId)).ToArray();
            _latestState = welcome.Playback;
            _reconnectTicket = welcome.ReconnectTicket is { Length: > 0 and <= 64 } ticket && ticket.All(char.IsAsciiHexDigit) ? ticket : null;
        }

        SetState(SessionState.Active);
        Raise(null, $"Вы в сеансе «{welcome.SessionName}»");
        var token = _stopping.Token;
        _loops.Add(Task.Run(() => ReceiveLoopAsync(token)));
        _loops.Add(Task.Run(() => ClockLoopAsync(token)));
        _loops.Add(Task.Run(() => TickLoopAsync(token)));
        if (welcome.GetValidRelay() is { } relay && _hostEndpoint is { } hostEndpoint)
        {
            // The relay runs on the host, so it is reached at the address that answered.
            _conference?.SetRelay(relay.ToTurnUri(hostEndpoint.Host));
        }

        _conference?.Start();
        _ = SyncConferenceAsync();
        if (welcome.Media is not null)
        {
            await PrepareMediaAsync(welcome.Media, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Plays a local copy instead of receiving the media, if the file matches the shared media.
    /// </summary>
    /// <param name="path">The local file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false"/> when the file differs from the shared media.</returns>
    public async Task<bool> UseLocalCopyAsync(string path, CancellationToken cancellationToken)
    {
        var media = _media ?? throw new InvalidOperationException("The host shares no media.");
        var local = await MediaFiles.DescribeAsync(path, cancellationToken).ConfigureAwait(false);
        if (local.Length != media.Length || !string.Equals(local.QuickId, media.QuickId, StringComparison.Ordinal))
        {
            return false;
        }

        await ReleaseRemoteMediaAsync().ConfigureAwait(false);
        await LoadAsync(new Uri(Path.GetFullPath(path)), cancellationToken).ConfigureAwait(false);
        _usesLocalCopy = true;
        if (_conferenceMedia is { IsAvailable: true } conference)
        {
            // A local copy is served to the other participants in full.
            _sharing = new ChunkSharing(media, conference, MediaFiles.OpenRead(path), _loggerFactory.CreateLogger<PeerChunkExchange>());
        }

        Raise(_identity.PeerId, "Воспроизводится локальная копия файла");
        return true;
    }

    /// <summary>
    /// Sends a playback intent to the host and applies it locally at once.
    /// </summary>
    /// <param name="request">The intent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the intent was sent.</returns>
    public async Task RequestAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var channel = _channel ?? throw new InvalidOperationException("The session is not joined.");
        await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _follower.ApplyLocalIntentAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _playerGate.Release();
        }

        await channel.SendAsync(new PlaybackRequestMessage(request), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a chat line or a reaction; it is shown when the host relays it back.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text or reaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the message was sent.</returns>
    public async Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("The session is not joined.");
        if (ChatMessage.Create(kind, text) is { } message)
        {
            await channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends a piece of a stroke drawn over the video; it is shown when the host relays it back.
    /// </summary>
    /// <param name="strokeId">The stroke.</param>
    /// <param name="phase">Which part of the stroke this is.</param>
    /// <param name="points">The new points.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the piece was sent.</returns>
    public async Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("The session is not joined.");
        if (DrawMessage.Create(strokeId, phase, points) is { } message)
        {
            await channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the current view of the session.
    /// </summary>
    /// <returns>The snapshot.</returns>
    public SessionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var participants = _participants.Select(p => p.IsLocal ? p with { Status = BuildStatus() } : p).ToArray();
            return new SessionSnapshot
            {
                IsHost = false,
                State = _state,
                SessionName = _sessionName,
                HostPeerId = _invite?.HostPeerId ?? default,
                Participants = participants,
                Playback = _latestState,
                Media = _media,
                Local = _localStatus,
                RoundTrip = _estimator.RoundTrip,
                ClockUncertainty = _estimator.Uncertainty,
                VerificationCode = _code,
                UsesLocalCopy = _usesLocalCopy,
            };
        }
    }

    /// <summary>
    /// Leaves the session.
    /// </summary>
    /// <returns>A task that completes when all resources are released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_channel is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _channel.SendAsync(new ByeMessage(null), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The host is gone already.
            }
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        await Task.WhenAll(_loops.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default))).ConfigureAwait(false);
        await _player.StopAsync().ConfigureAwait(false);
        if (_conference is not null)
        {
            await _conference.DisposeAsync().ConfigureAwait(false);
        }
        await ReleaseRemoteMediaAsync().ConfigureAwait(false);
        SetState(SessionState.Ended);
        _stopping.Dispose();
    }

    /// <summary>
    /// Connects to the first reachable endpoint of an invitation.
    /// </summary>
    /// <param name="invite">The invitation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The control stream.</returns>
    /// <exception cref="SessionJoinException">No endpoint answered.</exception>
    private async Task<PeerStream> ConnectAnyAsync(Invite invite, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var endpoint in invite.Endpoints)
        {
            try
            {
                var stream = await _connector.ConnectAsync(endpoint, invite.HostPeerId, StreamPurpose.Control, cancellationToken).ConfigureAwait(false);
                _hostEndpoint = endpoint;
                _logger.LogInformation("Connected to host at {Endpoint}", endpoint);
                return stream;
            }
            catch (PeerAuthenticationException ex)
            {
                throw new SessionJoinException(ex.Message + " Соединение прервано: возможна подмена узла.", ex);
            }
            catch (IOException ex)
            {
                errors.Add(ex.Message);
            }
        }

        throw new SessionJoinException("Ведущий недоступен ни по одному адресу:\n" + string.Join("\n", errors));
    }

    /// <summary>
    /// Processes messages of the host.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the connection ends.</returns>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var reason = "Соединение с ведущим потеряно.";
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await _channel!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                switch (message)
                {
                    case null:
                        return;
                    case ClockPongMessage pong:
                        _estimator.AddSample(pong.ClientSendTime, pong.HostReceiveTime, pong.HostSendTime, _monotonic.NowMicroseconds);
                        break;
                    case PlaybackStateMessage stateMessage:
                        OnState(stateMessage.State);
                        break;
                    case ConferenceSignalMessage signal when signal.Kind.Length <= 16 && signal.Payload.Length <= 64 * 1024:
                        await (_conference?.ReceiveAsync(signal.Peer, signal.Kind, signal.Payload) ?? Task.CompletedTask).ConfigureAwait(false);
                        break;
                    case ParticipantsMessage participants:
                        OnParticipants(participants);
                        await SyncConferenceAsync().ConfigureAwait(false);
                        break;
                    case MediaChangedMessage media when media.Media is not null:
                        media.Media.Validate();
                        await PrepareMediaAsync(media.Media, cancellationToken).ConfigureAwait(false);
                        break;
                    case ModerationMessage moderation:
                        if (_conference?.SwitchOff(moderation.Microphone, moderation.Camera) == true)
                        {
                            Raise(null, (moderation.Microphone, moderation.Camera) switch
                            {
                                (true, true) => "Ведущий выключил ваши микрофон и камеру. Включить их снова можете только вы.",
                                (true, false) => "Ведущий выключил ваш микрофон. Включить его снова можете только вы.",
                                _ => "Ведущий выключил вашу камеру. Включить её снова можете только вы.",
                            });
                        }

                        break;
                    case ChatMessage chat when chat.Sender is { } sender && chat.Sanitize() is { } clean:
                        OnChat(clean, sender);
                        break;
                    case DrawMessage stroke when stroke.Sender is { } author && stroke.Sanitize() is { } cleanStroke:
                        DrawReceived?.Invoke(this, new StrokeUpdate(author, cleanStroke.StrokeId, cleanStroke.Phase, cleanStroke.Points, author == _identity.PeerId));
                        break;
                    case ByeMessage bye:
                        reason = bye.Reason ?? "Ведущий завершил сеанс.";
                        return;
                    case RejectedMessage rejected:
                        reason = rejected.Message;
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or ObjectDisposedException)
        {
            _logger.LogWarning("Connection to host failed: {Reason}", ex.Message);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_disposed == 0)
            {
                SetState(SessionState.Ended);
                Raise(null, reason);
            }
        }
    }

    /// <summary>
    /// Shows a chat message relayed by the host.
    /// </summary>
    /// <param name="message">The clean message.</param>
    /// <param name="sender">The sender authenticated by the host.</param>
    private void OnChat(ChatMessage message, PeerId sender)
    {
        string name;
        lock (_gate)
        {
            name = _participants.FirstOrDefault(p => p.Info.PeerId == sender)?.Info.DisplayName ?? "Участник";
        }

        ChatReceived?.Invoke(
            this,
            new ChatEntry(message.Id, sender, name, message.Kind, message.Text, _timeProvider.GetLocalNow(), sender == _identity.PeerId));
    }

    /// <summary>
    /// Sends clock probes: a fast burst after joining, then periodically.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the session ends.</returns>
    private async Task ClockLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var probe = 0; !cancellationToken.IsCancellationRequested; probe++)
            {
                await _channel!.SendAsync(new ClockPingMessage(_monotonic.NowMicroseconds), cancellationToken).ConfigureAwait(false);
                var delay = probe < InitialProbes ? TimeSpan.FromMilliseconds(300) : _options.ClockProbeInterval;
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Runs the follower and sends status reports.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the session ends.</returns>
    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.TickInterval, _timeProvider);
        var lastReport = _timeProvider.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // The expected position depends on the clock offset: apply states only once it is known.
                    if (_clock.IsSynchronized && _latestState is { } latest
                        && (_follower.Applied is null || latest.Version > _follower.Applied.Version))
                    {
                        await _follower.ApplyAsync(latest, cancellationToken).ConfigureAwait(false);
                        Changed?.Invoke(this, EventArgs.Empty);
                    }

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

                if (_timeProvider.GetElapsedTime(lastReport) >= _options.StatusInterval)
                {
                    lastReport = _timeProvider.GetTimestamp();
                    await _channel!.SendAsync(new StatusReportMessage(BuildStatus()), cancellationToken).ConfigureAwait(false);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Sends a signal of the local conference through the host.
    /// </summary>
    /// <param name="signal">The signal addressed to a participant.</param>
    /// <returns>A task that completes when the signal was sent.</returns>
    private async Task SendConferenceSignalAsync(ConferenceSignal signal)
    {
        if (_channel is { } channel && _disposed == 0)
        {
            await channel.SendAsync(new ConferenceSignalMessage(signal.PeerId, signal.Kind, signal.Payload), _stopping.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Connects the cameras of the other participants.
    /// </summary>
    /// <returns>A task that completes when the set is updated.</returns>
    private Task SyncConferenceAsync()
    {
        if (_conference is null)
        {
            return Task.CompletedTask;
        }

        PeerId[] peers;
        lock (_gate)
        {
            peers = _participants.Select(p => p.Info.PeerId).ToArray();
        }

        return _conference.SyncAsync(peers);
    }

    /// <summary>
    /// Accepts a new authoritative state.
    /// </summary>
    /// <param name="state">The state.</param>
    private void OnState(PlaybackState state)
    {
        lock (_gate)
        {
            if (_latestState is not null && state.Version <= _latestState.Version)
            {
                return;
            }

            _latestState = state;
        }

        var name = _participants.FirstOrDefault(p => p.Info.PeerId == state.Origin)?.Info.DisplayName ?? "Участник";
        Raise(state.Origin, SessionTexts.Describe(state, name));
    }

    /// <summary>
    /// Accepts the participant list.
    /// </summary>
    /// <param name="message">The message.</param>
    private void OnParticipants(ParticipantsMessage message)
    {
        var statuses = message.Statuses.Take(SessionOptions.MaxParticipantsLimit * 2).ToDictionary(s => s.PeerId, s => s);
        lock (_gate)
        {
            _participants = message.Participants.Take(SessionOptions.MaxParticipantsLimit)
                .Select(p => new ParticipantView(p with { DisplayName = ParticipantInfo.NormalizeDisplayName(p.DisplayName) }, statuses.GetValueOrDefault(p.PeerId), p.PeerId == _identity.PeerId))
                .ToArray();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the shared media through the host.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player started loading.</returns>
    private async Task PrepareMediaAsync(MediaDescriptor media, CancellationToken cancellationToken)
    {
        await ReleaseRemoteMediaAsync().ConfigureAwait(false);
        var host = _invite!.HostPeerId;
        var endpoint = _hostEndpoint;
        var source = new RemoteChunkSource(
            media,
            async token => (await _connector.ConnectAsync(endpoint, host, StreamPurpose.MediaData, token).ConfigureAwait(false)).Stream,
            _mediaStreams,
            _loggerFactory.CreateLogger<RemoteChunkSource>());
        var cache = new ChunkCache();
        IChunkSource chunks = source;
        if (_conferenceMedia is { IsAvailable: true } conference)
        {
            // Other participants may already have chunks; they are checked against the host's hashes.
            var sharing = new ChunkSharing(media, conference, cache, _loggerFactory.CreateLogger<PeerChunkExchange>());
            var swarm = new SwarmChunkSource(source, sharing.Exchange, _loggerFactory.CreateLogger<SwarmChunkSource>());
            lock (_gate)
            {
                _sharing = sharing;
                _swarm = swarm;
            }

            chunks = swarm;
        }

        var reader = new CachedMediaReader(chunks, cache, new ReadAheadOptions(), _loggerFactory.CreateLogger<CachedMediaReader>());
        var uri = _uriRegistry.Register(() => new MediaReadStream(reader));
        lock (_gate)
        {
            _media = media;
            _reader = reader;
            _mediaUri = uri;
            _usesLocalCopy = false;
        }

        await LoadAsync(uri, cancellationToken).ConfigureAwait(false);
        Raise(host, $"Ведущий показывает «{media.FileName}»");
    }

    /// <summary>
    /// Loads a source into the player and re-applies the session state.
    /// </summary>
    /// <param name="uri">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player started loading.</returns>
    private async Task LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var start = _latestState?.ExpectedPositionAt(_clock.NowMicroseconds) ?? TimeSpan.Zero;
            _follower.Reset();
            await _player.LoadAsync(uri, start, paused: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _playerGate.Release();
        }
    }

    /// <summary>
    /// Releases the reader of the remote media.
    /// </summary>
    /// <returns>A task that completes when the reader is closed.</returns>
    private async Task ReleaseRemoteMediaAsync()
    {
        CachedMediaReader? reader;
        Uri? uri;
        ChunkSharing? sharing;
        lock (_gate)
        {
            reader = _reader;
            uri = _mediaUri;
            sharing = _sharing;
            _reader = null;
            _mediaUri = null;
            _sharing = null;
            _swarm = null;
        }

        if (sharing is not null)
        {
            await sharing.DisposeAsync().ConfigureAwait(false);
        }

        if (uri is not null)
        {
            _uriRegistry.Unregister(uri);
        }

        if (reader is not null)
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the status report of this device.
    /// </summary>
    /// <returns>The status.</returns>
    private ParticipantStatus BuildStatus()
    {
        var local = _localStatus;
        var roundTrip = _estimator.RoundTrip;
        return new ParticipantStatus
        {
            PeerId = _identity.PeerId,
            Position = local?.Snapshot.Position,
            Drift = local?.Drift ?? TimeSpan.Zero,
            CacheAhead = local?.Snapshot.CacheAhead ?? TimeSpan.Zero,
            IsBuffering = local?.Snapshot.IsBuffering ?? true,
            RoundTripMilliseconds = roundTrip is { } rtt ? (int)rtt.TotalMilliseconds : null,
            UsesLocalCopy = _usesLocalCopy,
            MicrophoneOff = _conference?.MicrophoneOff ?? true,
            CameraOff = _conference?.CameraOff ?? true,
            BytesFromPeers = _swarm?.BytesFromPeers ?? 0,
            BytesToPeers = _sharing?.Exchange.BytesSent ?? 0,
        };
    }

    /// <summary>
    /// Changes the connection state.
    /// </summary>
    /// <param name="state">The new state.</param>
    private void SetState(SessionState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises an event feed entry.
    /// </summary>
    /// <param name="actor">The participant, or <see langword="null"/>.</param>
    /// <param name="text">The text.</param>
    private void Raise(PeerId? actor, string text)
    {
        EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), actor, text));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
