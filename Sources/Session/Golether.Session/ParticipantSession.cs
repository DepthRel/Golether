using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Data.Enums;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Core.Time;
using Golether.Localization;
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
    private const int INITIAL_PROBES = 6;

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
    /// Lets one change of the played source run at a time: the media of the welcome, a new file announced by the
    /// host and a local copy chosen by the user may arrive together, and two of them interleaved would leave the
    /// reader of one and the player of the other.
    /// </summary>
    private readonly SemaphoreSlim _mediaGate = new(1, 1);

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
    /// Meets the host through its rendezvous server when no address answers directly, or <see langword="null"/>.
    /// </summary>
    private readonly IRendezvousConnector? _rendezvous;

    /// <summary>
    /// The head start of the direct addresses before a meeting through the rendezvous server begins: in one network
    /// they answer at once, and a meeting costs a round through the server. A reachable host answers well within
    /// these seconds; the direct attempts keep running after them, so a slow one still wins when it gets through.
    /// </summary>
    private static readonly TimeSpan _rendezvousHeadStart = TimeSpan.FromSeconds(4);

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
    /// The host endpoint that answered, or <see langword="null"/> when the host was met through the rendezvous server
    /// (every further stream is met the same way).
    /// </summary>
    private PeerEndpoint? _hostEndpoint;

    /// <summary>
    /// The local entrance to the relay of the host, or <see langword="null"/> when the relay is reached directly or
    /// not at all.
    /// </summary>
    private Transports.Relay.RelayTunnel? _relayTunnel;

    /// <summary>
    /// The shared stream that carries the connections of <see cref="_relayTunnel"/>, or <see langword="null"/> when
    /// each connection has a stream of its own.
    /// </summary>
    private Transports.Relay.SharedRelayRoute? _relayRoute;

    /// <summary>
    /// The media streams left idle by a film that was closed. The host serves any film over them, so the next film
    /// starts on them at once instead of opening new streams (through a rendezvous server, new meetings).
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentBag<Stream> _spareMediaStreams = [];

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
    /// The identifier of the showing of pictures, or <see langword="null"/> while the host shows none.
    /// </summary>
    private string? _galleryId;

    /// <summary>
    /// The name of the shown folder.
    /// </summary>
    private string _galleryTitle = string.Empty;

    /// <summary>
    /// The pictures received so far, by index; replaced, never changed, when a page arrives.
    /// </summary>
    private GalleryItem?[] _galleryItems = [];

    /// <summary>
    /// The index of the picture on everybody's screen.
    /// </summary>
    private int _galleryStage;

    /// <summary>
    /// The number of the latest decision of the host about the picture on everybody's screen.
    /// </summary>
    private long _galleryStageVersion;

    /// <summary>
    /// Who chose the picture on everybody's screen.
    /// </summary>
    private PeerId? _galleryStageOrigin;

    /// <summary>
    /// The picture this participant looks at alone, or <see langword="null"/> while it follows the common screen.
    /// </summary>
    private int? _galleryView;

    /// <summary>
    /// The small pictures asked of the host and not answered yet, by showing and index.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Catalog, int Index), TaskCompletionSource<byte[]?>> _thumbnailRequests = new();

    /// <summary>
    /// Limits the small pictures asked for at once, so the strip does not crowd the control stream.
    /// </summary>
    private readonly SemaphoreSlim _thumbnailSlots = new(6, 6);

    /// <summary>
    /// How long an answer with a small picture is waited for.
    /// </summary>
    private static readonly TimeSpan _thumbnailPatience = TimeSpan.FromSeconds(20);

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
    /// <param name="rendezvous">
    /// Meets the host through the rendezvous server of the invitation when no address answers directly, or
    /// <see langword="null"/> for direct connections only.
    /// </param>
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
        IConferenceMedia? conference = null,
        IRendezvousConnector? rendezvous = null)
    {
        _rendezvous = rendezvous;
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
    /// Raised when a participant's avatar arrived from the host, including this device's own. Raised on a background
    /// thread.
    /// </summary>
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <summary>
    /// Gets the participant name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets a value indicating whether the host itself closed the session for this device (ended the session, or
    /// removed or rejected the device) instead of the connection breaking. Coming back makes no sense then.
    /// </summary>
    public bool ClosedByHost { get; private set; }

    /// <summary>
    /// Gets why a session this device had been admitted to ended, in the words for the user, or
    /// <see langword="null"/> while it runs, after this device left by itself, and when joining never succeeded
    /// (that failure is reported by <see cref="JoinAsync"/>).
    /// </summary>
    public string? EndReason { get; private set; }

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

        if (invite.HostPeerId == _identity.PeerId)
        {
            // An invitation pasted on the device that made it: there is nobody to connect to.
            throw new SessionJoinException(Texts.Get("Session.Error.OwnInvite"));
        }

        if (!_returning && invite.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            throw new SessionJoinException(Texts.Get("Session.Error.InviteExpired"));
        }

        _invite = invite;
        _sessionName = invite.SessionName;
        _code = VerificationCode.Compute(invite.HostPeerId, _identity.PeerId, invite.Token);
        var stream = await ConnectAnyAsync(invite, cancellationToken).ConfigureAwait(false);
        _channel = new SessionMessageChannel(stream.Stream);
        await _channel.SendAsync(new HelloMessage(SessionMessage.PROTOCOL_VERSION, DisplayName, invite.Token) { Gallery = true }, cancellationToken).ConfigureAwait(false);

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
                throw new SessionJoinException(Texts.Get("Session.Error.ConnectionInterrupted"), ex);
            }

            switch (message)
            {
                case PendingApprovalMessage:
                    SetState(SessionState.AwaitingApproval);
                    continue;
                case RejectedMessage rejected:
                    // A host that did not answer in time or has no free place may be asked again; any other
                    // refusal is its decision.
                    ClosedByHost = rejected.Reason is not (RejectReason.ApprovalTimedOut or RejectReason.SessionFull);
                    SetState(SessionState.Ended);
                    throw new SessionJoinException(SessionTexts.DescribeRejection(rejected.Reason));
                case WelcomeMessage accepted:
                    welcome = accepted;
                    break;
                case null:
                    SetState(SessionState.Ended);
                    throw new SessionJoinException(Texts.Get("Session.Error.HostClosed"));
                default:
                    continue;
            }

            break;
        }

        // The welcome came from the network like everything else: its parts are checked before they are used.
        try
        {
            if (welcome.SessionName is null || welcome.Participants is null || welcome.Participants.Any(p => p is null) ||
                welcome.Playback is null || !welcome.Playback.IsValid())
            {
                throw new InvalidDataException("The welcome of the host is incomplete.");
            }

            welcome.Media?.Validate();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            SetState(SessionState.Ended);
            throw new SessionJoinException(Texts.Get("Session.Error.ConnectionInterrupted"), ex);
        }

        lock (_gate)
        {
            _sessionName = ParticipantInfo.NormalizeDisplayName(welcome.SessionName);
            _mediaStreams = Math.Clamp(welcome.MediaDataStreams, 1, 16);
            _participants = ToViews(welcome.Participants, []);
            _latestState = welcome.Playback;
            _reconnectTicket = welcome.ReconnectTicket is { Length: > 0 and <= 64 } ticket && ticket.All(char.IsAsciiHexDigit) ? ticket : null;
        }

        SetState(SessionState.Active);
        Raise(null, Texts.Format("Session.Event.YouJoined", _sessionName));
        var token = _stopping.Token;
        _loops.Add(Task.Run(() => ReceiveLoopAsync(token)));
        _loops.Add(Task.Run(() => ClockLoopAsync(token)));
        _loops.Add(Task.Run(() => TickLoopAsync(token)));
        if (_conference is not null && welcome.GetValidRelay() is { } relay)
        {
            if (welcome.RelayOverSession && StartRelayTunnel(welcome.RelayShared) is { } tunnel)
            {
                // The relay is reached by the route of the session, whichever it was: the cameras and voices are
                // pointed at the local end of the tunnel.
                _conference.SetRelay((relay with { Port = tunnel.Port }).ToTurnUri(System.Net.IPAddress.Loopback.ToString()));
            }
            else if (_hostEndpoint is { } hostEndpoint)
            {
                // A host of an earlier version: its relay is reached at the address that answered.
                _conference.SetRelay(relay.ToTurnUri(hostEndpoint.Host));
            }
        }

        // The STUN port of the host's rendezvous server tells the cameras and voices their public addresses, so they
        // reach the others directly through the routers as well.
        _conference?.SetStunServer(invite.Rendezvous?.StunUri);
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
        var media = _media ?? throw new InvalidOperationException(Texts.Get("Session.Error.NoMedia"));
        var local = await MediaFiles.DescribeAsync(path, cancellationToken).ConfigureAwait(false);
        if (local.Length != media.Length || !string.Equals(local.QuickId, media.QuickId, StringComparison.Ordinal))
        {
            return false;
        }

        await _mediaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_media, media))
            {
                // The host showed another file while this one was being compared.
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
        }
        finally
        {
            _mediaGate.Release();
        }

        Raise(_identity.PeerId, Texts.Get("Session.Event.LocalCopyPlaying"));
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
        var channel = _channel ?? throw new InvalidOperationException(Texts.Get("Session.Error.NotJoined"));
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
        var channel = _channel ?? throw new InvalidOperationException(Texts.Get("Session.Error.NotJoined"));
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
        var channel = _channel ?? throw new InvalidOperationException(Texts.Get("Session.Error.NotJoined"));
        if (DrawMessage.Create(strokeId, phase, points) is { } message)
        {
            await channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends this device's avatar to the host, which relays it to everybody; an empty picture clears it.
    /// </summary>
    /// <param name="image">The PNG bytes, or <see langword="null"/> to clear the avatar.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the avatar was sent.</returns>
    public async Task SendAvatarAsync(byte[]? image, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException(Texts.Get("Session.Error.NotJoined"));
        await channel.SendAsync(new AvatarMessage(image, null), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the host to put an item of the shown folder on everybody's screen; the host decides and tells everybody.
    /// </summary>
    /// <param name="catalogId">The showing the item belongs to.</param>
    /// <param name="index">The index of the item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the intention was sent.</returns>
    public async Task ShowItemAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException(Texts.Get("Session.Error.NotJoined"));
        await channel.SendAsync(new GalleryShowMessage(catalogId, index, 0, null), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Notes which picture this participant looks at alone; the others see it in the next status.
    /// </summary>
    /// <param name="index">The picture, or <see langword="null"/> when following the common screen.</param>
    public void SetGalleryView(int? index)
    {
        _galleryView = index;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Asks the host for the small picture of a picture.
    /// </summary>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The JPEG bytes, or <see langword="null"/> when the host has none or did not answer in time.</returns>
    public async Task<byte[]?> LoadThumbnailAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException(Texts.Get("Session.Error.NotJoined"));
        lock (_gate)
        {
            if (!string.Equals(_galleryId, catalogId, StringComparison.Ordinal) || index < 0 || index >= _galleryItems.Length)
            {
                return null;
            }
        }

        var key = (catalogId, index);
        await _thumbnailSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fresh = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = _thumbnailRequests.GetOrAdd(key, fresh);
            if (ReferenceEquals(waiting, fresh))
            {
                await channel.SendAsync(new ThumbnailMessage(catalogId, index, null), cancellationToken).ConfigureAwait(false);
            }

            return await waiting.Task.WaitAsync(_thumbnailPatience, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _thumbnailRequests.TryRemove(key, out _);
            return null;
        }
        finally
        {
            _thumbnailSlots.Release();
        }
    }

    /// <summary>
    /// Fetches a picture of the shown folder from the host, over the media data streams.
    /// </summary>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes of the picture, each piece checked against its hash.</returns>
    /// <exception cref="InvalidOperationException">The host shows something else now.</exception>
    public async Task<byte[]> LoadPictureAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        GalleryItem item;
        lock (_gate)
        {
            if (!string.Equals(_galleryId, catalogId, StringComparison.Ordinal) || index < 0 || index >= _galleryItems.Length ||
                _galleryItems[index] is not { Kind: GalleryItemKind.Picture } found)
            {
                throw new InvalidOperationException(Texts.Get("Session.Error.PictureGone"));
            }

            item = found;
        }

        var media = item.ToDescriptor();
        var source = new RemoteChunkSource(
            media,
            async token => _spareMediaStreams.TryTake(out var spare)
                ? spare
                : (await OpenStreamAsync(StreamPurpose.MediaData, token).ConfigureAwait(false)).Stream,
            Math.Min(_mediaStreams, 4),
            _loggerFactory.CreateLogger<RemoteChunkSource>())
        {
            KeepStream = KeepMediaStreamAsync,
        };
        await using (source.ConfigureAwait(false))
        {
            var chunks = await Task.WhenAll(Enumerable.Range(0, (int)media.ChunkCount).Select(i => source.GetChunkAsync(i, cancellationToken).AsTask()))
                .ConfigureAwait(false);
            var bytes = new byte[media.Length];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                chunk.CopyTo(bytes, offset);
                offset += chunk.Length;
            }

            return bytes;
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
                Gallery = BuildGallerySnapshot(),
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

        if (_relayTunnel is not null)
        {
            await _relayTunnel.DisposeAsync().ConfigureAwait(false);
        }

        if (_relayRoute is not null)
        {
            await _relayRoute.DisposeAsync().ConfigureAwait(false);
        }

        await ReleaseRemoteMediaAsync().ConfigureAwait(false);
        while (_spareMediaStreams.TryTake(out var spare))
        {
            await spare.DisposeAsync().ConfigureAwait(false);
        }

        SetState(SessionState.Ended);
        _stopping.Dispose();
    }

    /// <summary>
    /// Connects to the host by the first route that works: the addresses of the invitation one by one and, when the
    /// invitation names a rendezvous server, a meeting through it that starts after a short head start of the direct
    /// addresses or as soon as they all failed. The route that won is used for every further stream.
    /// </summary>
    /// <param name="invite">The invitation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The control stream.</returns>
    /// <exception cref="SessionJoinException">No route reached the host.</exception>
    private async Task<PeerStream> ConnectAnyAsync(Invite invite, CancellationToken cancellationToken)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var direct = ConnectDirectAsync(invite, errors, race.Token);
        var met = _rendezvous is { } rendezvous && invite.Rendezvous is { } point
            ? MeetAsync(rendezvous, point, invite.HostPeerId, direct, errors, race.Token)
            : Task.FromResult<(PeerStream Stream, PeerEndpoint? Endpoint)?>(null);
        var routes = new List<Task<(PeerStream Stream, PeerEndpoint? Endpoint)?>> { direct, met };
        (PeerStream Stream, PeerEndpoint? Endpoint)? winner = null;
        SessionJoinException? spoofing = null;
        while (routes.Count > 0)
        {
            var done = await Task.WhenAny(routes).ConfigureAwait(false);
            routes.Remove(done);
            (PeerStream Stream, PeerEndpoint? Endpoint)? route;
            try
            {
                route = await done.ConfigureAwait(false);
            }
            catch (SessionJoinException ex)
            {
                spoofing ??= ex;
                continue;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (route is null)
            {
                continue;
            }

            if (winner is null)
            {
                winner = route;
                await race.CancelAsync().ConfigureAwait(false);
            }
            else
            {
                await route.Value.Stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (winner is { } won)
        {
            // A route that verified the pinned key wins even if another address presented a foreign one.
            _hostEndpoint = won.Endpoint;
            return won.Stream;
        }

        // The addresses tried and why each failed go to the log, not to the screen: an address is nobody else's
        // business, and the screen may be shared.
        _logger.LogWarning("The host was not reached: {Errors}", string.Join("; ", errors));
        throw spoofing ?? new SessionJoinException(Texts.Get("Session.Error.HostUnreachable"));
    }

    /// <summary>
    /// Tries the addresses of an invitation one by one.
    /// </summary>
    /// <param name="invite">The invitation.</param>
    /// <param name="errors">Collects the reasons of the failures.</param>
    /// <param name="cancellationToken">Stops the attempts once another route won.</param>
    /// <returns>The stream and the address that answered, or <see langword="null"/> when none did.</returns>
    /// <exception cref="SessionJoinException">An address presented a foreign key and no other address was the host.</exception>
    private async Task<(PeerStream Stream, PeerEndpoint? Endpoint)?> ConnectDirectAsync(
        Invite invite,
        System.Collections.Concurrent.ConcurrentQueue<string> errors,
        CancellationToken cancellationToken)
    {
        SessionJoinException? foreignKey = null;
        foreach (var endpoint in invite.Endpoints)
        {
            try
            {
                var stream = await _connector.ConnectAsync(endpoint, invite.HostPeerId, StreamPurpose.Control, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Connected to host at {Endpoint}", endpoint);
                return (stream, endpoint);
            }
            catch (PeerAuthenticationException ex)
            {
                // Another device may answer at an address of the invitation (the same private address exists in
                // the network of the participant). The remaining addresses are still tried, each against the
                // pinned key; the warning is raised only when none of them is the host.
                _logger.LogWarning("The address {Endpoint} presented a foreign key", endpoint);
                foreignKey ??= new SessionJoinException(Texts.Format("Session.Error.PossibleSpoofing", ex.Message), ex);
            }
            catch (IOException ex)
            {
                errors.Enqueue(ex.Message);
            }
        }

        return foreignKey is null ? null : throw foreignKey;
    }

    /// <summary>
    /// Meets the host through its rendezvous server, after the head start of the direct addresses.
    /// </summary>
    /// <param name="rendezvous">Meets the host.</param>
    /// <param name="point">The server and the room of the host.</param>
    /// <param name="host">The pinned identifier of the host.</param>
    /// <param name="direct">The direct attempts; a meeting is not needed when they succeed.</param>
    /// <param name="errors">Collects the reasons of the failures.</param>
    /// <param name="cancellationToken">Stops the meeting once another route won.</param>
    /// <returns>The stream, or <see langword="null"/> when the meeting failed or was not needed.</returns>
    /// <exception cref="SessionJoinException">The met device presented a foreign key.</exception>
    private async Task<(PeerStream Stream, PeerEndpoint? Endpoint)?> MeetAsync(
        IRendezvousConnector rendezvous,
        RendezvousPoint point,
        PeerId host,
        Task<(PeerStream Stream, PeerEndpoint? Endpoint)?> direct,
        System.Collections.Concurrent.ConcurrentQueue<string> errors,
        CancellationToken cancellationToken)
    {
        await Task.WhenAny(direct, Task.Delay(_rendezvousHeadStart, _timeProvider, cancellationToken)).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested || direct is { IsCompletedSuccessfully: true, Result: not null })
        {
            return null;
        }

        try
        {
            var stream = await rendezvous.ConnectAsync(point, host, StreamPurpose.Control, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Met the host through the rendezvous server {Server}", point.Server);
            return (stream, null);
        }
        catch (PeerAuthenticationException ex)
        {
            throw new SessionJoinException(Texts.Format("Session.Error.PossibleSpoofing", ex.Message), ex);
        }
        catch (IOException ex)
        {
            errors.Enqueue(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Opens the local entrance to the relay of the host. The connections made to it travel over the route of the
    /// session: all in one shared stream when the host takes such, each in a stream of its own otherwise.
    /// </summary>
    /// <param name="shared">Whether the host takes one shared stream for all connections.</param>
    /// <returns>The tunnel, or <see langword="null"/> when no local port could be opened.</returns>
    private Transports.Relay.RelayTunnel? StartRelayTunnel(bool shared)
    {
        var log = _loggerFactory.CreateLogger<Transports.Relay.RelayTunnel>();
        Func<CancellationToken, Task<Stream>> open;
        if (shared)
        {
            var route = new Transports.Relay.SharedRelayRoute(
                async token => (await OpenStreamAsync(StreamPurpose.RelayShared, token).ConfigureAwait(false)).Stream,
                _timeProvider,
                log);
            _relayRoute = route;
            open = route.OpenAsync;
        }
        else
        {
            open = async token => (await OpenStreamAsync(StreamPurpose.Relay, token).ConfigureAwait(false)).Stream;
        }

        var tunnel = new Transports.Relay.RelayTunnel(open, log);
        try
        {
            tunnel.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _logger.LogWarning("The local entrance to the relay of the host was not opened: {Error}", ex.SocketErrorCode);
            return null;
        }

        _relayTunnel = tunnel;
        return tunnel;
    }

    /// <summary>
    /// Opens another stream to the host by the route the control stream took.
    /// </summary>
    /// <param name="purpose">The stream purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="IOException">The host could not be reached.</exception>
    private Task<PeerStream> OpenStreamAsync(StreamPurpose purpose, CancellationToken cancellationToken)
    {
        var invite = _invite ?? throw new InvalidOperationException("The session was not joined.");
        bool ended;
        lock (_gate)
        {
            ended = _state == SessionState.Ended;
        }

        if (ended || _disposed != 0)
        {
            // The player and the cameras do not know that the session is over and keep asking for streams. Each
            // would be one more connection attempt (through the rendezvous server, a whole meeting) made next to
            // the attempts to come back.
            return Task.FromException<PeerStream>(new IOException(Texts.Get("Session.Error.ConnectionLost")));
        }

        return _hostEndpoint is { } endpoint
            ? _connector.ConnectAsync(endpoint, invite.HostPeerId, purpose, cancellationToken)
            : _rendezvous is { } rendezvous && invite.Rendezvous is { } point
                ? rendezvous.ConnectAsync(point, invite.HostPeerId, purpose, cancellationToken)
                : throw new InvalidOperationException("The session has no route to the host.");
    }

    /// <summary>
    /// Processes messages of the host.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the connection ends.</returns>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var reason = Texts.Get("Session.Error.ConnectionLost");
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
                    case PlaybackStateMessage { State: not null } stateMessage:
                        OnState(stateMessage.State);
                        break;
                    case ConferenceSignalMessage { Kind.Length: <= 16, Payload.Length: <= 64 * 1024 } signal:
                        await (_conference?.ReceiveAsync(signal.Peer, signal.Kind, signal.Payload) ?? Task.CompletedTask).ConfigureAwait(false);
                        break;
                    case ParticipantsMessage { Participants: not null } participants:
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
                            Raise(null, Texts.Get((moderation.Microphone, moderation.Camera) switch
                            {
                                (true, true) => "Session.Event.SwitchedOffBoth",
                                (true, false) => "Session.Event.SwitchedOffMicrophone",
                                _ => "Session.Event.SwitchedOffCamera",
                            }));
                        }

                        break;
                    case ChatMessage chat when chat.Sender is { } sender && chat.Sanitize() is { } clean:
                        OnChat(clean, sender);
                        break;
                    case DrawMessage stroke when stroke.Sender is { } author && stroke.Sanitize() is { } cleanStroke:
                        DrawReceived?.Invoke(this, new StrokeUpdate(author, cleanStroke.StrokeId, cleanStroke.Phase, cleanStroke.Points, author == _identity.PeerId));
                        break;
                    case AvatarMessage avatar when avatar.Sender is { } owner && avatar.Sanitize() is { } cleanAvatar:
                        AvatarReceived?.Invoke(this, new AvatarUpdate(owner, cleanAvatar.Image, owner == _identity.PeerId));
                        break;
                    case GalleryMessage gallery when gallery.Sanitize() is { } page:
                        OnGallery(page);
                        break;
                    case GalleryShowMessage { Sender: { } chooser } show:
                        OnShow(show, chooser);
                        break;
                    case ThumbnailMessage thumbnail:
                        if (_thumbnailRequests.TryRemove((thumbnail.CatalogId, thumbnail.Index), out var waiting))
                        {
                            waiting.TrySetResult(thumbnail.Sanitize().Image);
                        }

                        break;
                    case ByeMessage:
                        // Texts do not travel over the network: whatever the host wrote, the end of the session is
                        // worded here, in the language of this side.
                        ClosedByHost = true;
                        reason = Texts.Get("Session.Error.HostEnded");
                        return;
                    case RejectedMessage rejected:
                        ClosedByHost = true;
                        reason = SessionTexts.DescribeRejection(rejected.Reason);
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
            // The host that said goodbye waits for this end to close before it closes its own: then nothing it was
            // sent is lost to a reset of the connection.
            if (ClosedByHost && _disposed == 0)
            {
                await _channel!.DisposeAsync().ConfigureAwait(false);
            }

            if (_disposed == 0)
            {
                // The reason is in place before anybody is told that the session ended.
                EndReason = reason;
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
            name = _participants.FirstOrDefault(p => p.Info.PeerId == sender)?.Info.DisplayName ?? Texts.Get("Session.DefaultParticipantName");
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
                var delay = probe < INITIAL_PROBES ? TimeSpan.FromMilliseconds(300) : _options.ClockProbeInterval;
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
                    if (_clock.IsSynchronized && _latestState is { } latest &&
                        (_follower.Applied is null || latest.Version > _follower.Applied.Version))
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
        if (!state.IsValid())
        {
            // A state that cannot be followed (an unknown mode, a position or a rate out of range) is skipped
            // instead of being handed to the player.
            _logger.LogWarning("Ignored an invalid playback state, version {Version}", state.Version);
            return;
        }

        lock (_gate)
        {
            if (_latestState is not null && state.Version <= _latestState.Version)
            {
                return;
            }

            _latestState = state;
        }

        var name = _participants.FirstOrDefault(p => p.Info.PeerId == state.Origin)?.Info.DisplayName ?? Texts.Get("Session.DefaultParticipantName");
        Raise(state.Origin, SessionTexts.Describe(state, name));
    }

    /// <summary>
    /// Accepts the participant list.
    /// </summary>
    /// <param name="message">The message.</param>
    private void OnParticipants(ParticipantsMessage message)
    {
        var views = ToViews(message.Participants, message.Statuses ?? []);
        lock (_gate)
        {
            _participants = views;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Turns the participant list of the host into the local view. The list comes from the network: it is cut to the
    /// largest session, names are normalized, the numbers of the statuses are bounded and broken entries are skipped.
    /// </summary>
    /// <param name="participants">The participants.</param>
    /// <param name="statuses">Their statuses.</param>
    /// <returns>The view of the participants.</returns>
    private ParticipantView[] ToViews(IReadOnlyList<ParticipantInfo> participants, IReadOnlyList<ParticipantStatus> statuses)
    {
        var known = new Dictionary<PeerId, ParticipantStatus>();
        foreach (var status in statuses.Take(SessionOptions.MAX_PARTICIPANTS_LIMIT * 2))
        {
            if (status is not null)
            {
                known[status.PeerId] = status.Sanitize();
            }
        }

        return participants.Take(SessionOptions.MAX_PARTICIPANTS_LIMIT)
            .Where(p => p is not null && !p.PeerId.IsEmpty)
            .Select(p => new ParticipantView(
                p with { DisplayName = ParticipantInfo.NormalizeDisplayName(p.DisplayName) },
                known.GetValueOrDefault(p.PeerId),
                p.PeerId == _identity.PeerId))
            .ToArray();
    }

    /// <summary>
    /// Opens the shared media through the host.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player started loading.</returns>
    private async Task PrepareMediaAsync(MediaDescriptor media, CancellationToken cancellationToken)
    {
        await _mediaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await OpenRemoteMediaAsync(media, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mediaGate.Release();
        }

        Raise(_invite!.HostPeerId, Texts.Format("Session.Event.HostShows", media.FileName));
    }

    /// <summary>
    /// Replaces the played source with the media of the host. Call under <see cref="_mediaGate"/>.
    /// </summary>
    /// <param name="media">The media.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player started loading.</returns>
    private async Task OpenRemoteMediaAsync(MediaDescriptor media, CancellationToken cancellationToken)
    {
        await ReleaseRemoteMediaAsync().ConfigureAwait(false);
        var source = new RemoteChunkSource(
            media,
            async token => _spareMediaStreams.TryTake(out var spare)
                ? spare
                : (await OpenStreamAsync(StreamPurpose.MediaData, token).ConfigureAwait(false)).Stream,
            _mediaStreams,
            _loggerFactory.CreateLogger<RemoteChunkSource>())
        {
            KeepStream = KeepMediaStreamAsync,
        };
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

        var reader = new CachedMediaReader(chunks, cache, ReadAheadOptions.For(_mediaStreams, media.ChunkSize), _loggerFactory.CreateLogger<CachedMediaReader>());
        var uri = _uriRegistry.Register(() => new MediaReadStream(reader));
        lock (_gate)
        {
            _media = media;
            _reader = reader;
            _mediaUri = uri;
            _usesLocalCopy = false;
        }

        await LoadAsync(uri, cancellationToken).ConfigureAwait(false);
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
    /// Takes an idle media stream of a film that is being closed, for the next film of the session.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns>A task that completes when the stream is kept or closed.</returns>
    private ValueTask KeepMediaStreamAsync(Stream stream)
    {
        bool ended;
        lock (_gate)
        {
            ended = _state == SessionState.Ended;
        }

        if (ended || _disposed != 0)
        {
            return stream.DisposeAsync();
        }

        _spareMediaStreams.Add(stream);
        return ValueTask.CompletedTask;
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
            GalleryView = _galleryView,
        };
    }

    /// <summary>
    /// Takes a page of the pictures the host shows, or the end of the showing.
    /// </summary>
    /// <param name="page">The checked page.</param>
    private void OnGallery(GalleryMessage page)
    {
        var started = false;
        lock (_gate)
        {
            if (page.CatalogId.Length == 0)
            {
                _galleryId = null;
                _galleryItems = [];
                _galleryView = null;
            }
            else
            {
                if (!string.Equals(_galleryId, page.CatalogId, StringComparison.Ordinal))
                {
                    started = true;
                    _galleryId = page.CatalogId;
                    _galleryTitle = page.Title;
                    _galleryItems = new GalleryItem?[page.Total];
                    _galleryStage = 0;
                    _galleryStageVersion = 0;
                    _galleryStageOrigin = null;
                    _galleryView = null;
                }

                if (page.Total == _galleryItems.Length)
                {
                    // A new array for every page: a snapshot handed out earlier keeps what it had.
                    var items = (GalleryItem?[])_galleryItems.Clone();
                    for (var i = 0; i < page.Items.Count; i++)
                    {
                        items[page.Offset + i] = page.Items[i];
                    }

                    _galleryItems = items;
                }
            }
        }

        // Small pictures still awaited for an earlier showing will not come.
        foreach (var key in _thumbnailRequests.Keys.Where(k => !string.Equals(k.Catalog, page.CatalogId, StringComparison.Ordinal)))
        {
            if (_thumbnailRequests.TryRemove(key, out var waiting))
            {
                waiting.TrySetResult(null);
            }
        }

        if (started)
        {
            Raise(_invite!.HostPeerId, Texts.Format("Session.Event.GalleryShown", NameOf(_invite.HostPeerId), page.Title, page.Total));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes the picture the host put on everybody's screen.
    /// </summary>
    /// <param name="show">The decision of the host.</param>
    /// <param name="chooser">Who chose the picture.</param>
    private void OnShow(GalleryShowMessage show, PeerId chooser)
    {
        string? name = null;
        lock (_gate)
        {
            if (!show.Fits(_galleryId, _galleryItems.Length) || show.Version <= _galleryStageVersion)
            {
                return;
            }

            // The first decision a newcomer hears is where the others already are, not a change to announce.
            if (_galleryStageVersion != 0 && show.Index != _galleryStage)
            {
                name = _galleryItems[show.Index]?.FileName;
            }

            _galleryStage = show.Index;
            _galleryStageVersion = show.Version;
            _galleryStageOrigin = chooser;
        }

        if (name is not null)
        {
            Raise(chooser, Texts.Format("Session.Event.MediaShown", NameOf(chooser), name));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the name of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <returns>The name.</returns>
    private string NameOf(PeerId peer)
    {
        lock (_gate)
        {
            return _participants.FirstOrDefault(p => p.Info.PeerId == peer)?.Info.DisplayName ??
                (peer == _invite?.HostPeerId ? _invite.HostName : Texts.Get("Session.DefaultParticipantName"));
        }
    }

    /// <summary>
    /// Returns the showing of pictures as the view sees it. Called under <see cref="_gate"/>.
    /// </summary>
    /// <returns>The showing, or <see langword="null"/> when there is none.</returns>
    private GallerySnapshot? BuildGallerySnapshot()
        => _galleryId is { } id
            ? new GallerySnapshot
            {
                CatalogId = id,
                Title = _galleryTitle,
                Total = _galleryItems.Length,
                Items = _galleryItems,
                Stage = _galleryStage,
                StageVersion = _galleryStageVersion,
                StageOrigin = _galleryStageOrigin,
            }
            : null;

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
