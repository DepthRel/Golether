using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Data.Entities;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Gallery;
using Golether.Media.Streaming.Protocol;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Security.Verification;
using Golether.Sync.Engine;
using Golether.Sync.Protocol;
using Golether.Transports;

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
    /// The pictures of the shown folder.
    /// </summary>
    private readonly SharedGallery _gallery = new();

    /// <summary>
    /// Makes the small pictures of the folder strip, or <see langword="null"/> when this device cannot.
    /// </summary>
    private readonly IThumbnailRenderer? _thumbnails;

    /// <summary>
    /// Decodes HEIC, AVIF and TIFF pictures and frames of videos of a shown folder, or <see langword="null"/>.
    /// </summary>
    private readonly IFrameExtractor? _frames;

    /// <summary>
    /// The folder where showings keep the JPEG files they make.
    /// </summary>
    private readonly string _galleryDirectory;

    /// <summary>
    /// Lets one change of the common screen through at a time: putting a video there loads it as the film.
    /// </summary>
    private readonly SemaphoreSlim _showGate = new(1, 1);

    /// <summary>
    /// The small pictures made for the current showing, by its identifier and the index of the picture: every
    /// participant asks for the same ones, and a picture is decoded once.
    /// </summary>
    private readonly ConcurrentDictionary<(string Catalog, int Index), Task<byte[]?>> _thumbnailCache = new();

    /// <summary>
    /// Limits the pictures decoded at once for small pictures, so the strip does not take every core of the host.
    /// </summary>
    private readonly SemaphoreSlim _thumbnailGate = new(2, 2);

    /// <summary>
    /// Guards the showing and the picture on everybody's screen.
    /// </summary>
    private readonly Lock _stageGate = new();

    /// <summary>
    /// The index of the picture on everybody's screen.
    /// </summary>
    private int _stage;

    /// <summary>
    /// The number of the latest decision about the picture on everybody's screen.
    /// </summary>
    private long _stageVersion;

    /// <summary>
    /// Who chose the picture on everybody's screen, or <see langword="null"/> for the first one of a showing.
    /// </summary>
    private PeerId? _stageOrigin;

    /// <summary>
    /// The picture the host looks at alone, or <see langword="null"/> while it follows the common screen.
    /// </summary>
    private int? _galleryView;

    /// <summary>
    /// Serves media data streams.
    /// </summary>
    private readonly ChunkServer _chunkServer;

    /// <summary>
    /// Admitted participants.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, Connection> _participants = new();

    /// <summary>
    /// The tickets that let admitted participants come back without a new invitation while the session runs.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, string> _reconnectTickets = new();

    /// <summary>
    /// How long a farewell (the end of the session, a removal) waits to reach a participant and for the participant
    /// to close its end of the connection.
    /// </summary>
    private static readonly TimeSpan _farewellTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The devices the host turned away (removed from the session or declined at the door) and are not to be let in
    /// by the invitations that existed at that moment: the number of the last invitation issued by then and the
    /// reason they are told. An invitation is shared by the whole company and stays valid for the others, so it is
    /// the device that is remembered, not the invitation that is cancelled. An invitation made later lets the
    /// device in again: this is not a ban.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, (long Barrier, RejectReason Reason)> _turnedAway = new();

    /// <summary>
    /// The latest avatar of each participant (this device included), kept so a participant who joins later is sent
    /// the avatars already chosen.
    /// </summary>
    private readonly ConcurrentDictionary<PeerId, byte[]> _avatars = new();

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
    /// <param name="thumbnails">Makes the small pictures of a shown folder, or <see langword="null"/>.</param>
    /// <param name="frames">Decodes HEIC, AVIF and TIFF pictures and frames of videos of a shown folder, or
    /// <see langword="null"/>.</param>
    /// <param name="galleryDirectory">The folder where showings keep the JPEG files they make; a folder of the
    /// system's temporary files when <see langword="null"/>.</param>
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
        IConferenceMedia? conference = null,
        IThumbnailRenderer? thumbnails = null,
        IFrameExtractor? frames = null,
        string? galleryDirectory = null)
    {
        _thumbnails = thumbnails;
        _frames = frames;
        _galleryDirectory = galleryDirectory ?? Path.Combine(Path.GetTempPath(), "golether-gallery");
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
        _chunkServer = new ChunkServer(_media, loggerFactory.CreateLogger<ChunkServer>(), _gallery);
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
    /// Raised for chat lines and reactions, including this device's own. Raised on a background thread.
    /// </summary>
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <summary>
    /// Raised for the strokes drawn over the video, including this device's own. Raised on a background thread.
    /// </summary>
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Raised when a participant's avatar arrived or changed, including this device's own. Raised on a background
    /// thread.
    /// </summary>
    public event EventHandler<AvatarUpdate>? AvatarReceived;

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
    /// Gets or sets the relay that serves the relay streams of admitted participants, or <see langword="null"/>.
    /// With it the cameras and voices of a participant reach the relay by the route of the session, so they work
    /// wherever the session does: no other port of the host has to be reachable.
    /// </summary>
    public Transports.Relay.IRelayStreamServer? RelayServer { get; set; }

    /// <summary>
    /// Gets or sets the rendezvous server and the room this host holds on it, or <see langword="null"/>. Set before
    /// <see cref="Start"/>: invitations then carry it, and the cameras and voices ask its STUN port for their
    /// public addresses.
    /// </summary>
    public RendezvousPoint? Rendezvous { get; set; }

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
        try
        {
            GalleryCatalog.DeleteLeftovers(_galleryDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The files of earlier showings were not deleted: {Reason}", ex.Message);
        }

        _loops.Add(Task.Run(() => AcceptLoopAsync(_stopping.Token)));
        _loops.Add(Task.Run(() => TickLoopAsync(_stopping.Token)));
        _conference?.SetStunServer(Rendezvous?.StunUri);
        _conference?.Start();
        Raise(null, Texts.Format("Session.Event.Created", SessionName, Port));
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
            Endpoints = endpoints.Take(Invite.MAX_ENDPOINTS).ToArray(),
            Token = token,
            ExpiresAt = expires,
            Rendezvous = Rendezvous,
        };
    }

    /// <summary>
    /// Shares a local file and loads it into the local player. A shown folder ends: the film takes the screen.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor.</returns>
    public async Task<MediaDescriptor> ShareMediaAsync(string path, CancellationToken cancellationToken)
    {
        var (descriptor, state) = await LoadFilmAsync(path, cancellationToken).ConfigureAwait(false);

        await CloseGalleryAsync().ConfigureAwait(false);
        await AnnounceFilmAsync(descriptor, state).ConfigureAwait(false);
        Raise(_identity.PeerId, Texts.Format("Session.Event.MediaShown", DisplayName, descriptor.FileName));
        return descriptor;
    }

    /// <summary>
    /// Shows the pictures and videos of a folder to everybody, starting with the first picture (the first video when
    /// there is no picture). A film that plays is paused and waits under the pictures.
    /// </summary>
    /// <param name="folder">The folder the host chose and confirmed.</param>
    /// <param name="stripMetadata">Whether the place, the camera and the comments are left out of the pictures.</param>
    /// <param name="progress">Told the number of files prepared so far.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when everybody was told.</returns>
    /// <exception cref="InvalidOperationException">No file of the folder can be shown.</exception>
    public async Task ShowGalleryAsync(GalleryFolder folder, bool stripMetadata, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var options = new GalleryOptions { StripMetadata = stripMetadata, Frames = _frames, WorkDirectory = _galleryDirectory };
        var catalog = await GalleryCatalog.CreateAsync(folder, options, progress, cancellationToken).ConfigureAwait(false);
        if (catalog.Items.Count == 0)
        {
            catalog.Dispose();
            throw new InvalidOperationException(Texts.Get("Session.Error.NoPictures"));
        }

        var first = 0;
        while (first < catalog.Items.Count - 1 && catalog.Items[first].Kind != GalleryItemKind.Picture)
        {
            first++;
        }

        first = catalog.Items[first].Kind == GalleryItemKind.Picture ? first : 0;
        await _showGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PrepareStageAsync(catalog, first, cancellationToken).ConfigureAwait(false);
            GalleryCatalog? previous;
            lock (_stageGate)
            {
                previous = _gallery.Share(catalog);
                _stage = first;
                _stageVersion++;
                _stageOrigin = null;
                _galleryView = null;
            }

            previous?.Dispose();
            _thumbnailCache.Clear();
        }
        catch
        {
            if (!ReferenceEquals(_gallery.Current, catalog))
            {
                catalog.Dispose();
            }

            throw;
        }
        finally
        {
            _showGate.Release();
        }

        await Task.WhenAll(_participants.Values.Where(c => c.SupportsGallery).Select(SendGalleryAsync)).ConfigureAwait(false);
        Raise(_identity.PeerId, Texts.Format("Session.Event.GalleryShown", DisplayName, catalog.Title, catalog.Items.Count));
    }

    /// <summary>
    /// Ends the showing of a folder: everybody goes back to the film.
    /// </summary>
    /// <returns>A task that completes when everybody was told.</returns>
    public async Task CloseGalleryAsync()
    {
        GalleryCatalog? previous;
        lock (_stageGate)
        {
            previous = _gallery.Share(null);
            _galleryView = null;
        }

        if (previous is null)
        {
            return;
        }

        previous.Dispose();
        _thumbnailCache.Clear();
        await BroadcastGalleryAsync(GalleryMessage.Closed()).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Puts an item of the shown folder on everybody's screen: a picture, or a video, which becomes the film.
    /// </summary>
    /// <param name="catalogId">The showing the item belongs to.</param>
    /// <param name="index">The index of the item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when everybody was told.</returns>
    public Task ShowItemAsync(string catalogId, int index, CancellationToken cancellationToken)
        => ApplyShowAsync(catalogId, index, _identity.PeerId, cancellationToken);

    /// <summary>
    /// Notes which picture the host looks at alone, for the marks the others see.
    /// </summary>
    /// <param name="index">The picture, or <see langword="null"/> when the host follows the common screen.</param>
    public void SetGalleryView(int? index)
    {
        _galleryView = index;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the bytes of a picture of the shown folder as participants receive them.
    /// </summary>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidOperationException">The host shows something else now.</exception>
    public Task<byte[]> LoadPictureAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        if (FindCatalog(catalogId, index) is not { } catalog || catalog.Items[index].Kind != GalleryItemKind.Picture)
        {
            throw new InvalidOperationException(Texts.Get("Session.Error.PictureGone"));
        }

        return Task.Run(
            () =>
            {
                using var stream = catalog.OpenRead(index);
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                return bytes;
            },
            cancellationToken);
    }

    /// <summary>
    /// Returns the small picture of a picture of the shown folder; it is made once and kept while the showing lasts.
    /// </summary>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The JPEG bytes, or <see langword="null"/> when there is none.</returns>
    public Task<byte[]?> LoadThumbnailAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        if (_thumbnails is null || FindCatalog(catalogId, index) is not { } catalog)
        {
            return Task.FromResult<byte[]?>(null);
        }

        return _thumbnailCache.GetOrAdd((catalogId, index), _ => RenderThumbnailAsync(catalog, index)).WaitAsync(cancellationToken);
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
    /// Sends a chat line or a reaction to everybody.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text or reaction.</param>
    /// <returns>A task that completes when the message was sent.</returns>
    public async Task SendChatAsync(ChatKind kind, string text)
    {
        if (ChatMessage.Create(kind, text) is not { } message)
        {
            return;
        }

        var signed = message with { Sender = _identity.PeerId };
        RaiseChat(signed);
        await BroadcastAsync(signed).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets this device's avatar and sends it to everybody; an empty picture clears it.
    /// </summary>
    /// <param name="image">The PNG bytes, or <see langword="null"/> to clear the avatar.</param>
    /// <returns>A task that completes when the avatar was sent.</returns>
    public async Task SetAvatarAsync(byte[]? image)
    {
        var clean = new AvatarMessage(image, _identity.PeerId).Sanitize();
        if (clean is null)
        {
            return;
        }

        RememberAvatar(_identity.PeerId, clean.Image);
        AvatarReceived?.Invoke(this, new AvatarUpdate(_identity.PeerId, clean.Image, true));
        await BroadcastAsync(clean).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a piece of a stroke drawn over the video to everybody.
    /// </summary>
    /// <param name="strokeId">The stroke.</param>
    /// <param name="phase">Which part of the stroke this is.</param>
    /// <param name="points">The new points.</param>
    /// <returns>A task that completes when the piece was sent.</returns>
    public async Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points)
    {
        if (DrawMessage.Create(strokeId, phase, points) is not { } message)
        {
            return;
        }

        var signed = message with { Sender = _identity.PeerId };
        RaiseDraw(signed);
        await BroadcastAsync(signed).ConfigureAwait(false);
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

        var key = (microphone, camera) switch
        {
            (true, true) => "Session.Event.HostSwitchesOffBoth",
            (true, false) => "Session.Event.HostSwitchesOffMicrophone",
            _ => "Session.Event.HostSwitchesOffCamera",
        };
        Raise(_identity.PeerId, Texts.Format(key, connection.Info.DisplayName));
    }

    /// <summary>
    /// Removes a participant. This is not a ban: the device may join again, but only with an invitation made after
    /// this moment. The ticket that would bring it back by itself is taken away, and the invitations that exist now,
    /// though still valid for the others, do not let it in.
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <returns>A task that completes when the participant was notified.</returns>
    public async Task RemoveParticipantAsync(PeerId peerId)
    {
        // The ticket goes first: with it the removed device would come back as a returning participant. The
        // invitations that exist now stay valid for the others, so the device itself is remembered.
        _reconnectTickets.TryRemove(peerId, out _);
        _turnedAway[peerId] = (_invites.LastSequence, RejectReason.Removed);
        if (_participants.TryRemove(peerId, out var connection))
        {
            connection.End();
            _avatars.TryRemove(peerId, out _);
            await TrySendAsync(connection, new RejectedMessage(RejectReason.Removed, nameof(RejectReason.Removed))).ConfigureAwait(false);
            Raise(peerId, Texts.Format("Session.Event.ParticipantRemoved", connection.Info.DisplayName));
            await SyncConferenceAsync().ConfigureAwait(false);
            await BroadcastParticipantsAsync().ConfigureAwait(false);

            await CloseAfterFarewellAsync(connection, Task.Delay(_farewellTime, _timeProvider)).ConfigureAwait(false);
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
            Gallery = BuildGallerySnapshot(),
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

        // Everybody is told that the session is over, so their screens go back to the start instead of trying to
        // come back. The farewell is given a short time: a participant on a stuck connection must not keep the
        // host from ending the session or closing the application.
        var deadline = Task.Delay(_farewellTime, _timeProvider);
        await Task.WhenAny(BroadcastAsync(new ByeMessage(null)), deadline).ConfigureAwait(false);
        await Task.WhenAll(_participants.Values.Select(c => CloseAfterFarewellAsync(c, deadline))).ConfigureAwait(false);

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
        _gallery.Share(null)?.Dispose();
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
                StreamPurpose.Relay or StreamPurpose.RelayShared => Task.Run(() => HandleRelayAsync(stream, cancellationToken), CancellationToken.None),
                _ => Task.Run(() => HandleMediaAsync(stream, cancellationToken), CancellationToken.None),
            };
        }
    }

    /// <summary>
    /// Hands a relay stream of an admitted participant to the relay: the TURN traffic of its cameras and voices.
    /// A shared stream carries all connections of the participant; each of its channels is served as a client.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>A task that completes when the stream ends.</returns>
    private async Task HandleRelayAsync(PeerStream stream, CancellationToken cancellationToken)
    {
        await using (stream.ConfigureAwait(false))
        {
            if (!_participants.TryGetValue(stream.RemotePeer, out var owner))
            {
                _logger.LogWarning("Relay stream from {Peer} refused: not admitted", stream.RemotePeer.ToShortString());
                return;
            }

            if (RelayServer is not { } relay || stream.LocalEndPoint is not { } local || stream.RemoteEndPoint is not { } remote)
            {
                _logger.LogWarning("Relay stream from {Peer} refused: no relay for it", stream.RemotePeer.ToShortString());
                return;
            }

            // The stream lives only as long as the admission: leaving, removal or a new connection closes it.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Lifetime);
            using var closeOnEnd = linked.Token.Register(() => stream.Stream.Dispose());
            _logger.LogInformation("Relay stream of {Peer} opened", stream.RemotePeer.ToShortString());
            try
            {
                // The relay is created at the address the participant reached, as for a client of the relay port.
                if (stream.Purpose == StreamPurpose.RelayShared)
                {
                    await using var shared = new Transports.Relay.RelayMultiplexer(
                        stream.Stream,
                        channel => relay.ServeAsync(channel, remote, local.Address, linked.Token),
                        _logger);
                    shared.Start();
                    await shared.Completion.ConfigureAwait(false);
                }
                else
                {
                    await relay.ServeAsync(stream.Stream, remote, local.Address, linked.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
            {
                _logger.LogDebug("Relay stream of {Peer} ended: {Reason}", stream.RemotePeer.ToShortString(), ex.Message);
            }
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
            _logger.LogInformation("Media stream of {Peer} opened", stream.RemotePeer.ToShortString());
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

                // A removed participant is still read until it closes its end, but nothing it says counts.
                if (connection.Lifetime.IsCancellationRequested)
                {
                    continue;
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
            if (connection is not null && _participants.TryRemove(new KeyValuePair<PeerId, Connection>(peer, connection)) && _ended == 0)
            {
                _avatars.TryRemove(peer, out _);
                Raise(peer, Texts.Format("Session.Event.ParticipantLeft", connection.Info.DisplayName));
                await SyncConferenceAsync().ConfigureAwait(false);
                await BroadcastParticipantsAsync().ConfigureAwait(false);
            }

            await channel.DisposeAsync().ConfigureAwait(false);
            connection?.MarkClosed();
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

        if (hello.Version != SessionMessage.PROTOCOL_VERSION)
        {
            await channel.SendAsync(new RejectedMessage(RejectReason.IncompatibleVersion, nameof(RejectReason.IncompatibleVersion)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (_participants.Count + 1 >= _options.MaxParticipants && !_participants.ContainsKey(peer))
        {
            await channel.SendAsync(new RejectedMessage(RejectReason.SessionFull, nameof(RejectReason.SessionFull)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        var contact = await FindContactAsync(peer, cancellationToken).ConfigureAwait(false);
        var returning = hello.InviteToken is { Length: > 0 } ticket &&
            _reconnectTickets.TryGetValue(peer, out var known) &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(known), System.Text.Encoding.UTF8.GetBytes(ticket));
        var trusted = contact?.IsTrusted == true || returning;
        var turnedAway = _turnedAway.TryGetValue(peer, out var barred);

        // A device the host turned away needs an invitation even when it is a trusted contact: trust saves the
        // invitation, it does not overrule the host.
        if (!trusted || turnedAway)
        {
            var check = _invites.TryUse(hello.InviteToken, out var issued);
            if (check != InviteCheckResult.Accepted)
            {
                await channel.SendAsync(new RejectedMessage(RejectReason.InvalidInvite, check.ToString()), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Rejected {Peer}: invite {Result}", peer.ToShortString(), check);
                return null;
            }

            // The invitation is fine for everybody else; this device needs one made after the host turned it away.
            if (turnedAway && issued <= barred.Barrier)
            {
                await channel.SendAsync(new RejectedMessage(barred.Reason, barred.Reason.ToString()), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Rejected {Peer}: turned away earlier, the invitation is not newer", peer.ToShortString());
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
            if (reason == RejectReason.Declined)
            {
                // "No" stays "no" for the invitations that exist: the shared link must not let the same device ask
                // again and again. A host who did not answer in time has decided nothing.
                _turnedAway[peer] = (_invites.LastSequence, RejectReason.Declined);
            }

            await channel.SendAsync(new RejectedMessage(reason, reason.ToString()), cancellationToken).ConfigureAwait(false);
            Raise(peer, Texts.Format("Session.Event.JoinDeclined", name));
            return null;
        }

        _turnedAway.TryRemove(peer, out _);
        await RememberContactAsync(peer, name, remoteAddress, cancellationToken).ConfigureAwait(false);

        // The ticket lets the participant come back after a broken connection; the host is asked again every time.
        var reconnectTicket = _reconnectTickets.GetOrAdd(peer, _ => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
        var connection = new Connection(new ParticipantInfo(peer, name, false), channel, _timeProvider.GetUtcNow())
        {
            SupportsGallery = hello.Gallery,
        };
        if (_participants.TryGetValue(peer, out var previous))
        {
            previous.End();
            await previous.Channel.DisposeAsync().ConfigureAwait(false);
        }

        _participants[peer] = connection;
        var participants = GetSnapshot().Participants.Select(p => p.Info).ToArray();
        await channel.SendAsync(
            new WelcomeMessage(SessionName, participants, _authority.Current, _media.Descriptor, _options.MediaDataStreams) { Relay = Relay, RelayOverSession = Relay is not null && RelayServer is not null, RelayShared = Relay is not null && RelayServer is not null, ReconnectTicket = reconnectTicket },
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Admitted {Peer}", peer.ToShortString());
        Raise(peer, Texts.Format("Session.Event.ParticipantJoined", name));
        _ = SyncConferenceAsync();
        await BroadcastParticipantsAsync().ConfigureAwait(false);

        // The pictures and the avatars go last and are not waited for: they are by far the largest messages of a
        // session, and on a slow route the newcomer would otherwise sit unanswered (no clock, no cameras) until they
        // have all arrived.
        _ = SendExtrasAsync(connection);
        return connection;
    }

    /// <summary>
    /// Brings a newcomer up to date with the shown pictures and the avatars, one after the other.
    /// </summary>
    /// <param name="connection">The newcomer.</param>
    /// <returns>A task that completes when everything was sent or the newcomer was dropped.</returns>
    private async Task SendExtrasAsync(Connection connection)
    {
        if (connection.SupportsGallery)
        {
            await SendGalleryAsync(connection).ConfigureAwait(false);
        }

        await SendAvatarsAsync(connection).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings a newcomer up to date with the avatars everybody already chose.
    /// </summary>
    /// <param name="connection">The newcomer.</param>
    /// <returns>A task that completes when the avatars were sent or the newcomer was dropped.</returns>
    private async Task SendAvatarsAsync(Connection connection)
    {
        foreach (var (owner, image) in _avatars)
        {
            if (connection.Lifetime.IsCancellationRequested)
            {
                return;
            }

            await TrySendAsync(connection, new AvatarMessage(image, owner)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Looks a device up among the contacts. The contacts are a convenience: when the store fails, the device is
    /// treated as unknown and goes through the invitation and the question to the host like anybody else.
    /// </summary>
    /// <param name="peer">The device.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>The contact, or <see langword="null"/> when there is none or the store failed.</returns>
    private async Task<ContactEntity?> FindContactAsync(PeerId peer, CancellationToken cancellationToken)
    {
        if (_contacts is null)
        {
            return null;
        }

        try
        {
            return await _contacts.FindAsync(peer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            _logger.LogWarning(ex, "The contacts could not be read");
            return null;
        }
    }

    /// <summary>
    /// Notes an admitted device among the contacts. A failure of the store must not turn away a participant the
    /// host has just approved.
    /// </summary>
    /// <param name="peer">The device.</param>
    /// <param name="name">The name it joined with.</param>
    /// <param name="remoteAddress">The address it came from.</param>
    /// <param name="cancellationToken">Stops the session.</param>
    /// <returns>A task that completes when the contact is stored or the attempt failed.</returns>
    private async Task RememberContactAsync(PeerId peer, string name, string? remoteAddress, CancellationToken cancellationToken)
    {
        if (_contacts is null)
        {
            return;
        }

        try
        {
            await _contacts.TouchAsync(peer, name, remoteAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            _logger.LogWarning(ex, "The contact of {Peer} could not be stored", peer.ToShortString());
        }
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

            case ConferenceSignalMessage { Kind.Length: <= 16, Payload.Length: <= 64 * 1024 } signal:
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

            case ChatMessage chat:
                if (chat.Sanitize() is { } clean &&
                    connection.ChatLimiter.TryAcquire(clean.Kind, _timeProvider.GetTimestamp(), _timeProvider))
                {
                    // The sender is the authenticated connection, never taken from the message.
                    var relayed = clean with { Sender = connection.Info.PeerId };
                    RaiseChat(relayed);
                    await BroadcastAsync(relayed).ConfigureAwait(false);
                }

                break;

            case DrawMessage stroke:
                if (stroke.Sanitize() is { } cleanStroke &&
                    connection.DrawLimiter.TryAcquire(_timeProvider.GetTimestamp(), _timeProvider))
                {
                    var relayed = cleanStroke with { Sender = connection.Info.PeerId };
                    RaiseDraw(relayed);
                    await BroadcastAsync(relayed).ConfigureAwait(false);
                }

                break;

            case AvatarMessage avatar:
                // A picture is the largest message a participant can make the host send to everybody, so it is
                // limited like the chat and the strokes.
                if (avatar.Sanitize() is { } cleanAvatar &&
                    connection.AvatarLimiter.TryAcquire(_timeProvider.GetTimestamp(), _timeProvider))
                {
                    // The owner is the authenticated connection, never taken from the message.
                    var signedAvatar = cleanAvatar with { Sender = connection.Info.PeerId };
                    RememberAvatar(connection.Info.PeerId, signedAvatar.Image);
                    AvatarReceived?.Invoke(this, new AvatarUpdate(connection.Info.PeerId, signedAvatar.Image, false));
                    await BroadcastAsync(signedAvatar).ConfigureAwait(false);
                }

                break;

            case GalleryShowMessage show when connection.SupportsGallery:
                // A choice of a picture changes the screen of everybody, so it is limited like the strokes.
                if (connection.ShowLimiter.TryAcquire(_timeProvider.GetTimestamp(), _timeProvider))
                {
                    _ = ApplyParticipantShowAsync(connection, show.CatalogId, show.Index);
                }

                break;

            case ThumbnailMessage { Image: null } request when connection.SupportsGallery:
                if (FindCatalog(request.CatalogId, request.Index) is not null &&
                    connection.ThumbnailLimiter.TryAcquire(_timeProvider.GetTimestamp(), _timeProvider))
                {
                    _ = SendThumbnailAsync(connection, request.CatalogId, request.Index);
                }

                break;

            case StatusReportMessage { Status: not null } report:
                // The numbers go into the arithmetic of the authority and to every participant: they are bounded first.
                connection.Status = report.Status.Sanitize() with { PeerId = connection.Info.PeerId };
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
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // The state is committed already. A failure of the local player must not keep it from the participants
            // (they would stay on the old one) nor end the connection of the participant who asked for it; the
            // follower applies it again on its next tick.
            _logger.LogWarning(ex, "Applying the state to the local player failed");
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

                try
                {
                    if (_localStatus?.Snapshot.Duration is { } duration)
                    {
                        _authority.Duration = duration;
                    }

                    if (_authority.EvaluateEnd() is { } ended)
                    {
                        await PublishAsync(ended, cancellationToken).ConfigureAwait(false);
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
                catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
                {
                    // One failed round (the player refusing a state, for example) must not stop the synchronization
                    // for the rest of the session: the next round tries again.
                    _logger.LogWarning(ex, "Session tick failed");
                }
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
            GalleryView = _galleryView,
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
            : Texts.Get("Session.DefaultParticipantName");

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
    /// Returns the current showing when a picture of it is meant.
    /// </summary>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <returns>The showing, or <see langword="null"/> when it is over or has no such picture.</returns>
    private GalleryCatalog? FindCatalog(string? catalogId, int index)
        => _gallery.Current is { } catalog && string.Equals(catalog.Id, catalogId, StringComparison.Ordinal) &&
           index >= 0 && index < catalog.Items.Count
            ? catalog
            : null;

    /// <summary>
    /// Makes the small picture of an item: of the picture, or of a frame of the video.
    /// </summary>
    /// <param name="catalog">The showing.</param>
    /// <param name="index">The index of the item.</param>
    /// <returns>The JPEG bytes, or <see langword="null"/> when the item cannot be decoded.</returns>
    private async Task<byte[]?> RenderThumbnailAsync(GalleryCatalog catalog, int index)
    {
        await _thumbnailGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var preview = await catalog.OpenPreviewAsync(index, _stopping.Token).ConfigureAwait(false);
            return preview is null ? null : await Task.Run(() => _thumbnails!.Render(preview)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogWarning("The small picture of {Index} was not made: {Reason}", index, ex.Message);
            return null;
        }
        finally
        {
            _thumbnailGate.Release();
        }
    }

    /// <summary>
    /// Loads a file into the local player and starts sharing it; nobody is told yet.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor and the new playback state.</returns>
    private async Task<(MediaDescriptor Descriptor, PlaybackState State)> LoadFilmAsync(string path, CancellationToken cancellationToken)
    {
        var descriptor = await MediaFiles.DescribeAsync(path, cancellationToken).ConfigureAwait(false);
        PlaybackState state;
        await _playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _follower.Reset();
            try
            {
                await _player.LoadAsync(new Uri(Path.GetFullPath(path)), TimeSpan.Zero, paused: true, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The player refused the file. Nothing was announced yet, so the session stays on what it had: the
                // participants are not told about a file the host cannot show, and the follower takes the current
                // state up again.
                try
                {
                    await _follower.ApplyAsync(_authority.Current, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The reason the file was refused is what the caller must see, not this second failure.
                    _logger.LogWarning(ex, "The state was not applied after a refused file");
                }

                throw;
            }

            _media.Share(path, descriptor);
            state = _authority.Reset();
            _authority.Duration = null;
            await _follower.ApplyAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _playerGate.Release();
        }

        return (descriptor, state);
    }

    /// <summary>
    /// Tells everybody about the new film and its state.
    /// </summary>
    /// <param name="descriptor">The film.</param>
    /// <param name="state">Its playback state.</param>
    /// <returns>A task that completes when all sends finished.</returns>
    private async Task AnnounceFilmAsync(MediaDescriptor descriptor, PlaybackState state)
    {
        await BroadcastAsync(new MediaChangedMessage(descriptor)).ConfigureAwait(false);
        await BroadcastAsync(new PlaybackStateMessage(state)).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the screen ready for an item: a picture pauses the film, which waits under it; a video becomes the film,
    /// unless it is the film already.
    /// </summary>
    /// <param name="catalog">The showing.</param>
    /// <param name="index">The index of the item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the screen is ready.</returns>
    private async Task PrepareStageAsync(GalleryCatalog catalog, int index, CancellationToken cancellationToken)
    {
        if (catalog.Items[index].Kind == GalleryItemKind.Video)
        {
            var path = catalog.PathOf(index);
            if (!string.Equals(_media.FilePath, path, StringComparison.Ordinal))
            {
                var (descriptor, state) = await LoadFilmAsync(path, cancellationToken).ConfigureAwait(false);
                await AnnounceFilmAsync(descriptor, state).ConfigureAwait(false);
            }

            return;
        }

        if (_authority.Current.State == PlayState.Playing)
        {
            await RequestAsync(new PlaybackRequest(PlaybackRequestKind.Pause, null), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Puts an item on everybody's screen: the host decides, numbers the decision and tells everybody.
    /// </summary>
    /// <param name="catalogId">The showing the item belongs to.</param>
    /// <param name="index">The index of the item.</param>
    /// <param name="origin">Who chose it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when everybody was told.</returns>
    private async Task ApplyShowAsync(string catalogId, int index, PeerId origin, CancellationToken cancellationToken)
    {
        await _showGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GalleryCatalog catalog;
            lock (_stageGate)
            {
                if (FindCatalog(catalogId, index) is not { } found || index == _stage)
                {
                    return;
                }

                catalog = found;
            }

            await PrepareStageAsync(catalog, index, cancellationToken).ConfigureAwait(false);
            GalleryShowMessage message;
            string name;
            lock (_stageGate)
            {
                if (!ReferenceEquals(_gallery.Current, catalog))
                {
                    // The showing ended while the video was loaded.
                    return;
                }

                _stage = index;
                _stageVersion++;
                _stageOrigin = origin;
                message = new GalleryShowMessage(catalog.Id, index, _stageVersion, origin);
                name = catalog.Items[index].FileName;
            }

            await BroadcastGalleryAsync(message).ConfigureAwait(false);
            Raise(origin, Texts.Format("Session.Event.MediaShown", NameOf(origin), name));
        }
        finally
        {
            _showGate.Release();
        }
    }

    /// <summary>
    /// Carries out a participant's choice of the common screen without holding up its other messages: a video takes a
    /// moment to load.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <param name="catalogId">The showing the item belongs to.</param>
    /// <param name="index">The index of the item.</param>
    /// <returns>A task that completes when the choice was carried out or refused.</returns>
    private async Task ApplyParticipantShowAsync(Connection connection, string catalogId, int index)
    {
        try
        {
            await ApplyShowAsync(catalogId, index, connection.Info.PeerId, connection.Lifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The participant left or the session ended.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The player refused the video: the screen stays as it was, and the host sees why in the log.
            _logger.LogWarning(ex, "The item {Index} chosen by {Peer} was not shown", index, connection.Info.PeerId);
        }
    }

    /// <summary>
    /// Sends the current showing to a participant: the pages of its pictures, then the picture on the screen.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <returns>A task that completes when everything was sent or the participant was dropped.</returns>
    private async Task SendGalleryAsync(Connection connection)
    {
        GalleryCatalog? catalog;
        GalleryShowMessage show;
        lock (_stageGate)
        {
            catalog = _gallery.Current;
            show = new GalleryShowMessage(catalog?.Id ?? string.Empty, _stage, _stageVersion, _stageOrigin ?? _identity.PeerId);
        }

        if (catalog is null)
        {
            return;
        }

        for (var offset = 0; offset < catalog.Items.Count; offset += GalleryMessage.PAGE_SIZE)
        {
            if (connection.Lifetime.IsCancellationRequested)
            {
                return;
            }

            var page = catalog.Items.Skip(offset).Take(GalleryMessage.PAGE_SIZE).ToArray();
            await TrySendAsync(connection, new GalleryMessage(catalog.Id, catalog.Title, catalog.Items.Count, offset, page)).ConfigureAwait(false);
        }

        await TrySendAsync(connection, show).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a participant's request for a small picture.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <param name="catalogId">The showing the picture belongs to.</param>
    /// <param name="index">The index of the picture.</param>
    /// <returns>A task that completes when the answer was sent.</returns>
    private async Task SendThumbnailAsync(Connection connection, string catalogId, int index)
    {
        try
        {
            var image = await LoadThumbnailAsync(catalogId, index, connection.Lifetime).ConfigureAwait(false);
            await TrySendAsync(connection, new ThumbnailMessage(catalogId, index, image).Sanitize()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The participant left.
        }
    }

    /// <summary>
    /// Sends a message of pictures to the participants that can show them.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when all sends finished.</returns>
    private Task BroadcastGalleryAsync(SessionMessage message)
        => Task.WhenAll(_participants.Values.Where(c => c.SupportsGallery).Select(c => TrySendAsync(c, message)));

    /// <summary>
    /// Returns the showing of pictures as the view sees it.
    /// </summary>
    /// <returns>The showing, or <see langword="null"/> when there is none.</returns>
    private GallerySnapshot? BuildGallerySnapshot()
    {
        lock (_stageGate)
        {
            return _gallery.Current is { } catalog
                ? new GallerySnapshot
                {
                    CatalogId = catalog.Id,
                    Title = catalog.Title,
                    Total = catalog.Items.Count,
                    Items = catalog.Items,
                    Stage = _stage,
                    StageVersion = _stageVersion,
                    StageOrigin = _stageOrigin,
                }
                : null;
        }
    }

    /// <summary>
    /// Sends a message to all participants; participants that fail are disconnected.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when all sends finished.</returns>
    private Task BroadcastAsync(SessionMessage message)
        => Task.WhenAll(_participants.Values.Select(c => TrySendAsync(c, message)));

    /// <summary>
    /// Sends a message with a timeout and disconnects the participant on failure. The timeout is what the writing
    /// itself may take, longer for a larger message: a message waiting behind an avatar on a slow route is not a
    /// reason to drop a participant whose connection works.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the send finished.</returns>
    private async Task TrySendAsync(Connection connection, SessionMessage message)
    {
        try
        {
            await connection.Channel.SendAsync(message, _options.SendTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogInformation("Dropping {Peer}: {Reason}", connection.Info.PeerId.ToShortString(), ex.Message);
            await connection.Channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the control channel of a participant that was told goodbye. The participant closes its end when it
    /// reads the farewell, and until then its messages are still read: closing this end with some of them unread
    /// would reset the connection, and a reset may throw away the farewell before the participant reads it (Windows
    /// does). A participant that does not close its end by the deadline is not waited for.
    /// </summary>
    /// <param name="connection">The participant.</param>
    /// <param name="deadline">Completes when waiting is over.</param>
    /// <returns>A task that completes when the channel is closed.</returns>
    private static async Task CloseAfterFarewellAsync(Connection connection, Task deadline)
    {
        await Task.WhenAny(connection.Closed, deadline).ConfigureAwait(false);

        await connection.Channel.DisposeAsync().ConfigureAwait(false);
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
    /// Stores or clears the remembered avatar of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="image">The PNG bytes, or <see langword="null"/> to forget the avatar.</param>
    private void RememberAvatar(PeerId peer, byte[]? image)
    {
        if (image is null)
        {
            _avatars.TryRemove(peer, out _);
        }
        else
        {
            _avatars[peer] = image;
        }
    }

    /// <summary>
    /// Shows a chat message on this device.
    /// </summary>
    /// <param name="message">The message with its sender.</param>
    private void RaiseChat(ChatMessage message)
    {
        var sender = message.Sender!.Value;
        ChatReceived?.Invoke(
            this,
            new ChatEntry(message.Id, sender, NameOf(sender), message.Kind, message.Text, _timeProvider.GetLocalNow(), sender == _identity.PeerId));
    }

    /// <summary>
    /// Shows a stroke on this device.
    /// </summary>
    /// <param name="message">The message with its sender.</param>
    private void RaiseDraw(DrawMessage message)
    {
        var sender = message.Sender!.Value;
        DrawReceived?.Invoke(this, new StrokeUpdate(sender, message.StrokeId, message.Phase, message.Points, sender == _identity.PeerId));
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
        /// Completes when the control channel has been read to its end and closed.
        /// </summary>
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        /// Gets the chat limit of the participant.
        /// </summary>
        public ChatRateLimiter ChatLimiter { get; } = new();

        /// <summary>
        /// The limit of stroke messages of the participant: a stroke sends a few times per second.
        /// </summary>
        public SlidingRateLimiter DrawLimiter { get; } = new(TimeSpan.FromSeconds(1), 40);

        /// <summary>
        /// The limit of avatar changes of the participant: a person picks a picture now and then, not many times a
        /// second, and each one is relayed to everybody.
        /// </summary>
        public SlidingRateLimiter AvatarLimiter { get; } = new(TimeSpan.FromSeconds(10), 5);

        /// <summary>
        /// Gets a value indicating whether the participant can show the pictures of a folder.
        /// </summary>
        public bool SupportsGallery { get; init; }

        /// <summary>
        /// The limit of the pictures the participant puts on everybody's screen: a person leafing through photos
        /// does it a few times a second at most.
        /// </summary>
        public SlidingRateLimiter ShowLimiter { get; } = new(TimeSpan.FromSeconds(1), 8);

        /// <summary>
        /// The limit of the small pictures the participant asks for: a strip scrolled quickly through a large
        /// folder asks for a few hundred.
        /// </summary>
        public SlidingRateLimiter ThumbnailLimiter { get; } = new(TimeSpan.FromSeconds(10), 400);

        /// <summary>
        /// Gets a token that is cancelled when the admission ends.
        /// </summary>
        public CancellationToken Lifetime => _lifetime.Token;

        /// <summary>
        /// Gets a task that completes when the control channel has been read to its end and closed.
        /// </summary>
        public Task Closed => _closed.Task;

        /// <summary>
        /// Ends the admission: closes the media streams of the participant.
        /// </summary>
        public void End() => _lifetime.Cancel();

        /// <summary>
        /// Notes that the control channel has been read to its end and closed.
        /// </summary>
        public void MarkClosed() => _closed.TrySetResult();
    }
}
