using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Playback;
using Golether.Core.Time;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.Transports.PortMapping;
using Golether.Transports.Relay;
using Golether.Transports.Tls;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Services;

/// <summary>
/// Starts, joins and controls watch sessions for the UI.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Raised when the session view changed. Raised on a background thread.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised for the event feed. Raised on a background thread.
    /// </summary>
    event EventHandler<SessionEvent>? EventRaised;

    /// <summary>
    /// Gets a value indicating whether a session is running.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets a value indicating whether this device hosts the session.
    /// </summary>
    bool IsHost { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a hosted session asks the router to open its port.
    /// </summary>
    bool OpenRouterPort { get; set; }

    /// <summary>
    /// Starts hosting a session.
    /// </summary>
    /// <param name="sessionName">The session name.</param>
    /// <param name="displayName">The host name.</param>
    /// <param name="port">The listening port.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the session listens.</returns>
    Task StartHostingAsync(string sessionName, string displayName, int port, CancellationToken cancellationToken);

    /// <summary>
    /// Shares a media file (host only).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the file is shared.</returns>
    Task ShareMediaAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an invitation link (host only).
    /// </summary>
    /// <param name="additionalEndpoints">Addresses entered by the user, listed first.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The invitation.</returns>
    Invite CreateInvite(IReadOnlyList<PeerEndpoint> additionalEndpoints, TimeSpan lifetime);

    /// <summary>
    /// Joins a session.
    /// </summary>
    /// <param name="inviteLink">The invitation link.</param>
    /// <param name="displayName">The participant name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the host admitted this device.</returns>
    /// <exception cref="FormatException">The link is invalid.</exception>
    /// <exception cref="SessionJoinException">Joining failed.</exception>
    Task JoinAsync(string inviteLink, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Plays a local copy of the shared file (participant only).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false"/> when the file differs.</returns>
    Task<bool> UseLocalCopyAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a playback intent.
    /// </summary>
    /// <param name="request">The intent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the intent was sent.</returns>
    Task RequestAsync(PlaybackRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current view, or <see langword="null"/> without session.
    /// </summary>
    /// <returns>The snapshot.</returns>
    SessionSnapshot? GetSnapshot();

    /// <summary>
    /// Switches devices of a participant off (host only).
    /// </summary>
    /// <param name="peerId">The participant.</param>
    /// <param name="microphone">Whether to switch the microphone off.</param>
    /// <param name="camera">Whether to switch the camera off.</param>
    /// <returns>A task that completes when the request was sent.</returns>
    Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera);

    /// <summary>
    /// Leaves or ends the session.
    /// </summary>
    /// <returns>A task that completes when the session is closed.</returns>
    Task LeaveAsync();
}

/// <summary>
/// <see cref="ISessionService"/> over <see cref="HostSession"/> and <see cref="ParticipantSession"/>.
/// </summary>
public sealed class SessionService : ISessionService, IAsyncDisposable
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
    /// The router mapping of the hosted session.
    /// </summary>
    private PortMappingLease? _portLease;

    /// <summary>
    /// The router mapping of the relay port.
    /// </summary>
    private PortMappingLease? _relayLease;

    /// <summary>
    /// The TURN relay of the hosted session.
    /// </summary>
    private TurnServer? _relay;

    /// <summary>
    /// The session options.
    /// </summary>
    private readonly SessionOptions _options = new();

    /// <summary>
    /// The hosted session.
    /// </summary>
    private HostSession? _host;

    /// <summary>
    /// The joined session.
    /// </summary>
    private ParticipantSession? _participant;

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
    public SessionService(
        DeviceIdentity identity,
        PlayerHost player,
        IAdmissionPrompt prompt,
        IContactStore contacts,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IConferenceMedia conference,
        PortMapper? portMapper = null)
    {
        _portMapper = portMapper;
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
    public bool IsActive => _host is not null || _participant is not null;

    /// <inheritdoc />
    public bool IsHost => _host is not null;

    /// <inheritdoc />
    public bool OpenRouterPort { get; set; } = true;

    /// <inheritdoc />
    public Task StartHostingAsync(string sessionName, string displayName, int port, CancellationToken cancellationToken)
    {
        EnsureIdle();
        var listener = new TlsPeerListener(_identity, new TlsTransportOptions { Port = port }, _loggerFactory.CreateLogger<TlsPeerListener>());
        try
        {
            listener.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _ = listener.DisposeAsync();
            throw new InvalidOperationException($"Порт {port} занят или недоступен: {ex.Message}", ex);
        }

        var host = new HostSession(
            _identity,
            listener,
            new InviteRegistry(_timeProvider),
            new AdmissionService(_prompt, new AdmissionOptions(), _timeProvider),
            _contacts,
            _player,
            StopwatchMonotonicClock.Instance,
            _timeProvider,
            sessionName,
            displayName,
            _options,
            _loggerFactory,
            _conference);
        _relay = StartRelay(listener.Port);
        host.Relay = _relay?.Credentials;
        Attach(host);
        _host = host;
        host.Start();
        if (_portMapper is not null && OpenRouterPort)
        {
            _ = OpenPortAsync(host, host.Port, _relay?.Port);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShareMediaAsync(string path, CancellationToken cancellationToken)
        => (_host ?? throw new InvalidOperationException("Файл показывает только ведущий.")).ShareMediaAsync(path, cancellationToken);

    /// <inheritdoc />
    public Invite CreateInvite(IReadOnlyList<PeerEndpoint> additionalEndpoints, TimeSpan lifetime)
    {
        var host = _host ?? throw new InvalidOperationException("Приглашать может только ведущий.");
        var mapping = _portLease?.Mapping;
        var external = mapping is { IsPubliclyReachable: true, ExternalAddress: { } address }
            ? [new PeerEndpoint(address.ToString(), mapping.ExternalPort)]
            : Array.Empty<PeerEndpoint>();
        return host.CreateInvite(EndpointDiscovery.Discover(host.Port, [.. additionalEndpoints, .. external]), lifetime);
    }

    /// <inheritdoc />
    public async Task JoinAsync(string inviteLink, string displayName, CancellationToken cancellationToken)
    {
        EnsureIdle();
        var invite = Invite.ParseLink(inviteLink);
        var participant = new ParticipantSession(
            _identity,
            new TlsPeerConnector(_identity, new TlsTransportOptions()),
            _player,
            new MpvMediaUriRegistry(),
            StopwatchMonotonicClock.Instance,
            _timeProvider,
            displayName,
            _options,
            _loggerFactory,
            _conference);
        Attach(participant);
        _participant = participant;
        try
        {
            await participant.JoinAsync(invite, cancellationToken).ConfigureAwait(false);
            await _contacts.TouchAsync(invite.HostPeerId, invite.HostName, null, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _participant = null;
            await participant.DisposeAsync().ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> UseLocalCopyAsync(string path, CancellationToken cancellationToken)
        => (_participant ?? throw new InvalidOperationException("Локальная копия доступна только участнику.")).UseLocalCopyAsync(path, cancellationToken);

    /// <inheritdoc />
    public Task RequestAsync(PlaybackRequest request, CancellationToken cancellationToken)
        => _host is not null ? _host.RequestAsync(request, cancellationToken)
            : _participant is not null ? _participant.RequestAsync(request, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
        => (_host ?? throw new InvalidOperationException("ÐÑÐºÐ»ÑÑÐ°ÑÑ ÑÑÑÑÐ¾Ð¹ÑÑÐ²Ð° ÑÑÐ°ÑÑÐ½Ð¸ÐºÐ¾Ð² Ð¼Ð¾Ð¶ÐµÑ ÑÐ¾Ð»ÑÐºÐ¾ Ð²ÐµÐ´ÑÑÐ¸Ð¹."))
            .SwitchOffParticipantDevicesAsync(peerId, microphone, camera);

    /// <inheritdoc />
    public SessionSnapshot? GetSnapshot() => _host?.GetSnapshot() ?? _participant?.GetSnapshot();

    /// <inheritdoc />
    public async Task LeaveAsync()
    {
        var host = Interlocked.Exchange(ref _host, null);
        var participant = Interlocked.Exchange(ref _participant, null);
        foreach (var lease in new[] { Interlocked.Exchange(ref _portLease, null), Interlocked.Exchange(ref _relayLease, null) })
        {
            if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }

        var relay = Interlocked.Exchange(ref _relay, null);
        if (relay is not null)
        {
            await relay.DisposeAsync().ConfigureAwait(false);
        }

        if (host is not null)
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }

        if (participant is not null)
        {
            await participant.DisposeAsync().ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(LeaveAsync());

    /// <summary>
    /// Opens the listening port on the router and reports the result in the event feed.
    /// </summary>
    /// <param name="host">The hosted session.</param>
    /// <param name="port">The listening port.</param>
    /// <param name="relayPort">The port of the TURN relay, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the router answered or gave up.</returns>
    private async Task OpenPortAsync(HostSession host, int port, int? relayPort)
    {
        if (relayPort is { } relay)
        {
            var relayLease = await TryMapAsync(relay).ConfigureAwait(false);
            if (relayLease is not null && (_host != host || Interlocked.CompareExchange(ref _relayLease, relayLease, null) is not null))
            {
                await relayLease.DisposeAsync().ConfigureAwait(false);
            }
            else if (relayLease is { Mapping.ExternalPort: var external } && external != relay)
            {
                _loggerFactory.CreateLogger<SessionService>().LogWarning("The router moved the relay port {Port} to {External}; relayed cameras from other networks will not connect", relay, external);
            }
        }

        PortMappingLease? lease;
        try
        {
            lease = await _portMapper!.MapAsync(port, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lease = null;
            System.Diagnostics.Debug.WriteLine(ex);
        }

        if (lease is not null && (_host != host || Interlocked.CompareExchange(ref _portLease, lease, null) is not null))
        {
            // The session ended while the router was answering.
            await lease.DisposeAsync().ConfigureAwait(false);
            return;
        }

        var mapping = lease?.Mapping;
        var text = mapping switch
        {
            null => $"Роутер не открыл порт {port} автоматически. Участникам из других сетей нужен проброс порта вручную или туннель AWG.",
            { IsPubliclyReachable: true } => $"Порт открыт на роутере ({mapping.Method}): {mapping.ExternalAddress}:{mapping.ExternalPort}. Адрес добавляется в приглашения.",
            _ => $"Роутер открыл порт ({mapping.Method}), но его внешний адрес {mapping.ExternalAddress?.ToString() ?? "неизвестен"} не публичный (NAT провайдера). Для других сетей нужен туннель AWG.",
        };
        if (_host == host)
        {
            EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), null, text));
        }
    }

    /// <summary>
    /// Asks the router to open a port.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns>The lease, or <see langword="null"/>.</returns>
    private async Task<PortMappingLease?> TryMapAsync(int port)
    {
        try
        {
            return await _portMapper!.MapAsync(port, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _loggerFactory.CreateLogger<SessionService>().LogDebug(ex, "Port {Port} was not opened", port);
            return null;
        }
    }

    /// <summary>
    /// Starts the TURN relay for cameras and voices on the port after the session port, or any free port.
    /// </summary>
    /// <param name="sessionPort">The session port.</param>
    /// <returns>The relay, or <see langword="null"/> when no port could be used.</returns>
    private TurnServer? StartRelay(int sessionPort)
    {
        foreach (var port in new[] { sessionPort < 65535 ? sessionPort + 1 : 0, 0 })
        {
            var relay = new TurnServer(
                new TurnServerOptions { Credentials = RelayCredentials.CreateRandom(port) },
                _timeProvider,
                _loggerFactory.CreateLogger<TurnServer>());
            try
            {
                relay.Start();
                return relay;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                _loggerFactory.CreateLogger<SessionService>().LogInformation("Relay port {Port} is busy: {Error}", port, ex.SocketErrorCode);
                _ = relay.DisposeAsync();
            }
        }

        return null;
    }

    /// <summary>
    /// Throws when a session is running.
    /// </summary>
    private void EnsureIdle()
    {
        if (IsActive)
        {
            throw new InvalidOperationException("Сначала завершите текущий сеанс.");
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
    }

    /// <summary>
    /// Forwards the events of a participant session.
    /// </summary>
    /// <param name="session">The session.</param>
    private void Attach(ParticipantSession session)
    {
        session.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        session.EventRaised += (_, e) => EventRaised?.Invoke(this, e);
    }
}
