using Microsoft.Extensions.Logging;
using Golether.Client.Enums;
using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Session.Hosting;
using Golether.Sync.Protocol;
using Golether.Transports.PortMapping;
using Golether.Transports.Rendezvous;
using Golether.Transports.Tls;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="ISessionService"/> over <see cref="HostSession"/> and <see cref="ParticipantSession"/>.
/// </summary>
public sealed class SessionService : ISessionService, IReconnectionClient, IAsyncDisposable
{
    /// <summary>
    /// The local device.
    /// </summary>
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// The player.
    /// </summary>
    private readonly PlayerHost _player;

    /// <summary>
    /// The admission prompt.
    /// </summary>
    private readonly IAdmissionPrompt _prompt;

    /// <summary>
    /// The contacts.
    /// </summary>
    private readonly IContactStore _contacts;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The logger factory.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Cameras and voices.
    /// </summary>
    private readonly IConferenceMedia _conference;

    /// <summary>
    /// Opens the listening port on the router, or <see langword="null"/>.
    /// </summary>
    private readonly PortMapper? _portMapper;

    /// <summary>
    /// Finds the rendezvous server of the project, or <see langword="null"/> when it is never looked up.
    /// </summary>
    private readonly IRendezvousDirectory? _rendezvousDirectory;

    /// <summary>
    /// The session options.
    /// </summary>
    private readonly SessionOptions _options = new();

    /// <summary>
    /// Makes the small pictures of a folder the host shows.
    /// </summary>
    private readonly SkiaPictureCodec _thumbnails = new();

    /// <summary>
    /// Decodes HEIC, AVIF and TIFF pictures and frames of videos of a shown folder, or <see langword="null"/>.
    /// </summary>
    private readonly IFrameExtractor? _frames;

    /// <summary>
    /// The folder where a shown folder keeps the JPEG files the host makes, or <see langword="null"/> for a folder
    /// of the system's temporary files.
    /// </summary>
    private readonly string? _galleryDirectory;

    /// <summary>
    /// The hosted session on its network: the session, its relay, its room and the ports on the router.
    /// </summary>
    private SessionHosting? _hosting;

    /// <summary>
    /// The joined session.
    /// </summary>
    private ParticipantSession? _participant;

    /// <summary>
    /// The name this device joined with, used when coming back.
    /// </summary>
    private string _joinName = string.Empty;

    /// <summary>
    /// The avatar this device showed last, or <see langword="null"/> without one. The host forgets the avatar of a
    /// participant whose connection broke, so it is sent again when the session is restored.
    /// </summary>
    private byte[]? _avatar;

    /// <summary>
    /// The invitation with the ticket that brings this device back, or <see langword="null"/>.
    /// </summary>
    private Invite? _reconnectInvite;

    /// <summary>
    /// Stops the reconnection attempts.
    /// </summary>
    private CancellationTokenSource? _reconnecting;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionService"/> class.
    /// </summary>
    /// <param name="identity">The local device.</param>
    /// <param name="player">The player.</param>
    /// <param name="prompt">The admission prompt.</param>
    /// <param name="contacts">The contacts.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="conference">Cameras and voices.</param>
    /// <param name="portMapper">Opens the listening port on the router, or <see langword="null"/>.</param>
    /// <param name="rendezvousDirectory">Finds the rendezvous server of the project, or <see langword="null"/>.</param>
    /// <param name="frames">Decodes HEIC, AVIF and TIFF pictures and frames of videos of a shown folder, or
    /// <see langword="null"/>.</param>
    /// <param name="galleryDirectory">The folder where a shown folder keeps the JPEG files the host makes, or
    /// <see langword="null"/>.</param>
    public SessionService(
        DeviceIdentity identity,
        PlayerHost player,
        IAdmissionPrompt prompt,
        IContactStore contacts,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IConferenceMedia conference,
        PortMapper? portMapper = null,
        IRendezvousDirectory? rendezvousDirectory = null,
        IFrameExtractor? frames = null,
        string? galleryDirectory = null)
    {
        _frames = frames;
        _galleryDirectory = galleryDirectory;
        _portMapper = portMapper;
        _rendezvousDirectory = rendezvousDirectory;
        _conference = conference;
        _identity = identity;
        _player = player;
        _prompt = prompt;
        _contacts = contacts;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<SessionEvent>? EventRaised;

    /// <inheritdoc />
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <inheritdoc />
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <inheritdoc />
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <inheritdoc />
    public event EventHandler<string>? ClosedByHost;

    /// <inheritdoc />
    /// <remarks>
    /// A joined session that has ended and is not being restored does not count: the start screen is shown again by
    /// then, and the next session releases what is left of it (see <see cref="ReleaseEndedAsync"/>).
    /// </remarks>
    public bool IsActive
        => _hosting is not null ||
           (_participant is { } participant && (_reconnecting is not null || participant.GetSnapshot().State != SessionState.Ended));

    /// <inheritdoc />
    public bool IsHost => _hosting is not null;

    /// <summary>
    /// Gets the hosted session, or <see langword="null"/> when this device does not host one.
    /// </summary>
    private HostSession? Host => _hosting?.Session;

    /// <inheritdoc />
    public string ReconnectMessage { get; private set; } = string.Empty;

    /// <inheritdoc />
    public bool OpenRouterPort { get; set; } = true;

    /// <inheritdoc />
    public RendezvousMode RendezvousMode { get; set; } = RendezvousMode.Manual;

    /// <inheritdoc />
    public string RendezvousServer { get; set; } = string.Empty;

    /// <inheritdoc />
    public async Task StartHostingAsync(string sessionName, string displayName, int port, CancellationToken cancellationToken)
    {
        await ReleaseEndedAsync().ConfigureAwait(false);
        EnsureIdle();
        var mode = RendezvousMode;
        var server = await FindRendezvousServerAsync(mode, cancellationToken).ConfigureAwait(false);

        // A fresh room for every session: an old invitation does not lead to a new session.
        var rendezvous = server is { } found ? new RendezvousPoint(found, RendezvousPoint.CreateRoom()) : null;

        var hosting = SessionHosting.Create(new HostingSetup
        {
            Identity = _identity,
            Prompt = _prompt,
            Contacts = _contacts,
            Player = _player,
            TimeProvider = _timeProvider,
            LoggerFactory = _loggerFactory,
            Conference = _conference,
            SessionName = sessionName,
            DisplayName = displayName,
            Port = port,
            Options = _options,
            Thumbnails = _thumbnails,
            Frames = _frames,
            GalleryDirectory = _galleryDirectory,
            Rendezvous = rendezvous,
            PortMapper = OpenRouterPort ? _portMapper : null,
        });
        Attach(hosting.Session);
        hosting.EventRaised += (_, e) => EventRaised?.Invoke(this, e);
        _hosting = hosting;
        hosting.Start();
        if (rendezvous is null && mode == RendezvousMode.Automatic)
        {
            // The session works without the server; the host learns why guests from other networks may not get in.
            EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), null, Texts.Get("Session.Rendezvous.NotFound")));
        }

        ReportFirewall();
    }

    /// <summary>
    /// Finds the rendezvous server of a new session according to the chosen mode.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The server, or <see langword="null"/> when the session runs without one.</returns>
    /// <exception cref="InvalidOperationException">The address typed by the user is not an address.</exception>
    private async Task<PeerEndpoint?> FindRendezvousServerAsync(RendezvousMode mode, CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case RendezvousMode.Automatic:
                return _rendezvousDirectory is { } directory
                    ? await directory.FindAsync(cancellationToken).ConfigureAwait(false)
                    : null;
            case RendezvousMode.Manual when !string.IsNullOrWhiteSpace(RendezvousServer):
                return PeerEndpoint.TryParse(RendezvousServer.Trim(), out var typed)
                    ? typed
                    : throw new InvalidOperationException(Texts.Get("Session.Error.BadRendezvousServer"));
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public Task ShareMediaAsync(string path, CancellationToken cancellationToken)
        => (Host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostShares"))).ShareMediaAsync(path, cancellationToken);

    /// <inheritdoc />
    public Invite CreateInvite(IReadOnlyList<PeerEndpoint> additionalEndpoints, TimeSpan lifetime)
        => (_hosting ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostInvites"))).CreateInvite(additionalEndpoints, lifetime);

    /// <inheritdoc />
    public async Task JoinAsync(string inviteLink, string displayName, CancellationToken cancellationToken)
    {
        await ReleaseEndedAsync().ConfigureAwait(false);
        EnsureIdle();
        var invite = Invite.ParseLink(inviteLink);
        var participant = CreateParticipant(displayName);
        _participant = participant;
        try
        {
            await participant.JoinAsync(invite, cancellationToken).ConfigureAwait(false);
            _joinName = displayName;
            _reconnectInvite = participant.ReconnectInvite;
            ReconnectMessage = string.Empty;
        }
        catch
        {
            _participant = null;
            _reconnectInvite = null;
            await participant.DisposeAsync().ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }

        try
        {
            // The contacts are a convenience: a failure of the store must not end a session that has just begun.
            await _contacts.TouchAsync(invite.HostPeerId, invite.HostName, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            _loggerFactory.CreateLogger<SessionService>().LogWarning(ex, "The contact of the host could not be stored");
        }
    }

    /// <inheritdoc />
    public Task<bool> UseLocalCopyAsync(string path, CancellationToken cancellationToken)
        => (_participant ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyParticipantCopy"))).UseLocalCopyAsync(path, cancellationToken);

    /// <inheritdoc />
    public Task RequestAsync(PlaybackRequest request, CancellationToken cancellationToken)
        => Host is not null ? Host.RequestAsync(request, cancellationToken)
            : _participant is not null ? _participant.RequestAsync(request, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
        => (Host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostModerates")))
            .SwitchOffParticipantDevicesAsync(peerId, microphone, camera);

    /// <inheritdoc />
    public Task RemoveParticipantAsync(PeerId peerId)
        => (Host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostModerates")))
            .RemoveParticipantAsync(peerId);

    /// <inheritdoc />
    public Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
        => Host is not null ? Host.SendChatAsync(kind, text)
            : _participant is not null ? _participant.SendChatAsync(kind, text, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
        => Host is not null ? Host.SendDrawAsync(strokeId, phase, points)
            : _participant is not null ? _participant.SendDrawAsync(strokeId, phase, points, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public bool CanDecodePictures => _frames?.IsAvailable == true;

    /// <inheritdoc />
    public Task ShowGalleryAsync(GalleryFolder folder, bool stripMetadata, IProgress<int>? progress, CancellationToken cancellationToken)
        => (Host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostShares"))).ShowGalleryAsync(folder, stripMetadata, progress, cancellationToken);

    /// <inheritdoc />
    public Task CloseGalleryAsync()
        => (Host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostShares"))).CloseGalleryAsync();

    /// <inheritdoc />
    public Task ShowItemAsync(string catalogId, int index, CancellationToken cancellationToken)
        => Host is not null ? Host.ShowItemAsync(catalogId, index, cancellationToken)
            : _participant is not null ? _participant.ShowItemAsync(catalogId, index, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public void SetGalleryView(int? index)
    {
        Host?.SetGalleryView(index);
        _participant?.SetGalleryView(index);
    }

    /// <inheritdoc />
    public Task<byte[]?> LoadThumbnailAsync(string catalogId, int index, CancellationToken cancellationToken)
        => Host is not null ? Host.LoadThumbnailAsync(catalogId, index, cancellationToken)
            : _participant is not null ? _participant.LoadThumbnailAsync(catalogId, index, cancellationToken)
            : Task.FromResult<byte[]?>(null);

    /// <inheritdoc />
    public Task<byte[]> LoadPictureAsync(string catalogId, int index, CancellationToken cancellationToken)
        => Host is not null ? Host.LoadPictureAsync(catalogId, index, cancellationToken)
            : _participant is not null ? _participant.LoadPictureAsync(catalogId, index, cancellationToken)
            : Task.FromException<byte[]>(new InvalidOperationException(Texts.Get("Session.Error.NotJoined")));

    /// <inheritdoc />
    public Task SetAvatarAsync(byte[]? image, CancellationToken cancellationToken)
    {
        _avatar = image;
        return Host is not null ? Host.SetAvatarAsync(image)
            : _participant is not null ? _participant.SendAvatarAsync(image, cancellationToken)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A joined session that has ended and is not being restored has nothing to show any more: like
    /// <see cref="IsActive"/>, the snapshot is gone, so the view clears the participants, the chat and the strokes
    /// instead of carrying them into the next session.
    /// </remarks>
    public SessionSnapshot? GetSnapshot()
    {
        if (Host is { } host)
        {
            return host.GetSnapshot();
        }

        if (_participant is not { } participant)
        {
            return null;
        }

        var snapshot = participant.GetSnapshot();
        return snapshot.State == SessionState.Ended && _reconnecting is null ? null : snapshot;
    }

    /// <inheritdoc />
    public async Task LeaveAsync()
    {
        var reconnecting = Interlocked.Exchange(ref _reconnecting, null);
        if (reconnecting is not null)
        {
            await reconnecting.CancelAsync().ConfigureAwait(false);
        }

        _reconnectInvite = null;
        ReconnectMessage = string.Empty;
        var hosting = Interlocked.Exchange(ref _hosting, null);
        var participant = Interlocked.Exchange(ref _participant, null);

        // Every part is released on its own: one that fails must not leave the others behind (a session that keeps
        // its port, a mapping that stays on the router).
        await ReleaseAsync(hosting).ConfigureAwait(false);
        await ReleaseAsync(participant).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Releases a part of a session, logging a failure instead of passing it on.
    /// </summary>
    /// <param name="part">The part, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the part is released or the attempt failed.</returns>
    private async Task ReleaseAsync(IAsyncDisposable? part)
    {
        if (part is null)
        {
            return;
        }

        try
        {
            await part.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _loggerFactory.CreateLogger<SessionService>().LogWarning(ex, "Releasing {Part} failed", part.GetType().Name);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(LeaveAsync());

    /// <summary>
    /// Tells the user when the Windows firewall would drop the participants. A fresh tunnel adapter counts as a
    /// public network, and without a rule for the program nobody gets in, however well the two computers see each
    /// other.
    /// </summary>
    private void ReportFirewall()
    {
        if (WindowsFirewall.Check() != FirewallState.Missing)
        {
            return;
        }

        FirewallBlocked = true;
        EventRaised?.Invoke(this, new SessionEvent(
            _timeProvider.GetLocalNow(),
            null,
            Texts.Get("Session.Firewall.Blocked")));
    }

    /// <inheritdoc />
    public bool FirewallBlocked { get; private set; }

    /// <inheritdoc />
    public async Task<bool> AllowFirewallAsync(CancellationToken cancellationToken)
    {
        if (WindowsFirewall.CurrentProgram() is not { } program)
        {
            return false;
        }

        try
        {
            var result = await new Golether.Tunnels.AmneziaWG.Control.ElevatedProcessRunner()
                .RunAsync(program, WindowsFirewall.BuildArguments(WindowsFirewall.ALLOW_VERB), TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != WindowsFirewall.SUCCESS)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _loggerFactory.CreateLogger<SessionService>().LogDebug(ex, "The firewall rule was not added");
            return false;
        }

        FirewallBlocked = WindowsFirewall.Check() == FirewallState.Missing;
        if (!FirewallBlocked)
        {
            EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), null, Texts.Get("Session.Firewall.Allowed")));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return !FirewallBlocked;
    }

    /// <summary>
    /// Starts reconnecting after the connection to the host broke: the ticket from the session is used, so no new
    /// invitation is needed, and the host is asked again on every attempt.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    private void StartReconnecting(ParticipantSession session)
    {
        // Only a broken connection is restored: when the host ended the session or removed this device, knocking
        // again would only bother the host.
        if (!ReferenceEquals(_participant, session) || session.ClosedByHost || _reconnectInvite is null || _reconnecting is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _reconnecting = cancellation;
        _ = ReconnectLoopAsync(session, cancellation);
    }

    /// <summary>
    /// Tries to come back to the session, waiting longer after every failed attempt.
    /// </summary>
    /// <param name="ended">The session that ended.</param>
    /// <param name="cancellation">Stops the attempts.</param>
    /// <returns>A task that completes when the session is back or the attempts stop.</returns>
    private async Task ReconnectLoopAsync(ParticipantSession ended, CancellationTokenSource cancellation)
    {
        try
        {
            var reconnector = new ParticipantReconnector(this, _timeProvider, _loggerFactory.CreateLogger<ParticipantReconnector>());
            await reconnector.RunAsync(ended, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_reconnecting, cancellation))
            {
                _reconnecting = null;
            }

            ReconnectMessage = string.Empty;
            cancellation.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    Invite? IReconnectionClient.GetTicket(ParticipantSession ended) => ReferenceEquals(_participant, ended) ? _reconnectInvite : null;

    /// <inheritdoc />
    ParticipantSession IReconnectionClient.CreateSession() => CreateParticipant(_joinName);

    /// <inheritdoc />
    async Task<bool> IReconnectionClient.AdoptAsync(ParticipantSession ended, ParticipantSession session, Invite ticket, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _participant, session, ended), ended))
        {
            return false;
        }

        _reconnectInvite = session.ReconnectInvite ?? ticket;
        if (_avatar is { Length: > 0 } avatar)
        {
            try
            {
                await session.SendAvatarAsync(avatar, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The connection broke again: the next restoring sends the picture.
            }
        }

        return true;
    }

    /// <inheritdoc />
    void IReconnectionClient.Report(string text) => Report(text);

    /// <summary>
    /// Shows the state of the reconnection in the interface and in the event feed.
    /// </summary>
    /// <param name="text">The text.</param>
    private void Report(string text)
    {
        ReconnectMessage = text;
        EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), null, text));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a participant session for this device.
    /// </summary>
    /// <param name="displayName">The name shown to others.</param>
    /// <returns>The session.</returns>
    private ParticipantSession CreateParticipant(string displayName)
    {
        var connector = new TlsPeerConnector(_identity, new TlsTransportOptions());
        var participant = new ParticipantSession(
            _identity,
            connector,
            _player,
            new MpvMediaUriRegistry(),
            SystemMonotonicClock.Instance,
            _timeProvider,
            displayName,
            _options,
            _loggerFactory,
            _conference,
            new RendezvousPeerConnector(connector, new RendezvousDialer(logger: _loggerFactory.CreateLogger<RendezvousDialer>())));
        Attach(participant);
        return participant;
    }

    /// <summary>
    /// Releases a joined session that has ended and is not being restored, so that a new session can start. Without
    /// it the ended session would stay "current" for ever: the start screen is shown, but hosting and joining would
    /// be refused until the application is restarted.
    /// </summary>
    /// <returns>A task that completes when the ended session is released.</returns>
    private async Task ReleaseEndedAsync()
    {
        if (_reconnecting is null &&
            _participant is { } ended &&
            ended.GetSnapshot().State == SessionState.Ended &&
            ReferenceEquals(Interlocked.CompareExchange(ref _participant, null, ended), ended))
        {
            _reconnectInvite = null;
            await ended.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Throws when a session is running.
    /// </summary>
    private void EnsureIdle()
    {
        if (IsActive)
        {
            throw new InvalidOperationException(Texts.Get("Session.Error.EndCurrentFirst"));
        }
    }

    /// <summary>
    /// Forwards the events of a host session.
    /// </summary>
    /// <param name="session">The session.</param>
    private void Attach(HostSession session)
    {
        session.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        session.EventRaised += (_, e) => EventRaised?.Invoke(this, e);
        session.ChatReceived += (_, e) => ChatReceived?.Invoke(this, e);
        session.DrawReceived += (_, e) => DrawReceived?.Invoke(this, e);
        session.AvatarReceived += (_, e) => AvatarReceived?.Invoke(this, e);
    }

    /// <summary>
    /// Forwards the events of a participant session.
    /// </summary>
    /// <param name="session">The session.</param>
    private void Attach(ParticipantSession session)
    {
        var told = 0;
        session.Changed += (_, _) =>
        {
            if (session.GetSnapshot().State == SessionState.Ended)
            {
                StartReconnecting(session);

                // The host ended the session or removed this device: nothing will be restored, and the event feed
                // disappears together with the session view, so the reason is told once on its own.
                if (session is { ClosedByHost: true, EndReason: { } reason } && Interlocked.Exchange(ref told, 1) == 0)
                {
                    ClosedByHost?.Invoke(this, reason);
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        };
        session.EventRaised += (_, e) => EventRaised?.Invoke(this, e);
        session.ChatReceived += (_, e) => ChatReceived?.Invoke(this, e);
        session.DrawReceived += (_, e) => DrawReceived?.Invoke(this, e);
        session.AvatarReceived += (_, e) => AvatarReceived?.Invoke(this, e);
    }
}
