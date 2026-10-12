using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Security.Admission;
using Golether.Security.Identity;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.Transports.PortMapping;
using Golether.Transports.Relay;
using Golether.Transports.Rendezvous;
using Golether.Transports.Tls;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Services;

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
    /// Holds the room of the hosted session on the rendezvous server, or <see langword="null"/>.
    /// </summary>
    private RendezvousHost? _rendezvousHost;

    /// <summary>
    /// The session options.
    /// </summary>
    private readonly SessionOptions _options = new();

    /// <summary>
    /// The number of reconnection attempts after the connection to the host broke.
    /// </summary>
    private const int ReconnectAttempts = 8;

    /// <summary>
    /// The waits before the attempts; the last one repeats.
    /// </summary>
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>
    /// The hosted session.
    /// </summary>
    private HostSession? _host;

    /// <summary>
    /// The joined session.
    /// </summary>
    private ParticipantSession? _participant;

    /// <summary>
    /// The name this device joined with, used when coming back.
    /// </summary>
    private string _joinName = string.Empty;

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
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <inheritdoc />
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <inheritdoc />
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <inheritdoc />
    /// <remarks>
    /// A joined session that has ended and is not being restored does not count: the start screen is shown again by
    /// then, and the next session releases what is left of it (see <see cref="ReleaseEndedAsync"/>).
    /// </remarks>
    public bool IsActive
        => _host is not null
           || (_participant is { } participant && (_reconnecting is not null || participant.GetSnapshot().State != SessionState.Ended));

    /// <inheritdoc />
    public bool IsHost => _host is not null;

    /// <inheritdoc />
    public string ReconnectMessage { get; private set; } = string.Empty;

    /// <inheritdoc />
    public bool OpenRouterPort { get; set; } = true;

    /// <inheritdoc />
    public string RendezvousServer { get; set; } = string.Empty;

    /// <inheritdoc />
    public async Task StartHostingAsync(string sessionName, string displayName, int port, CancellationToken cancellationToken)
    {
        await ReleaseEndedAsync().ConfigureAwait(false);
        EnsureIdle();
        RendezvousPoint? rendezvous = null;
        if (!string.IsNullOrWhiteSpace(RendezvousServer))
        {
            // A fresh room for every session: an old invitation does not lead to a new session.
            rendezvous = PeerEndpoint.TryParse(RendezvousServer.Trim(), out var server)
                ? new RendezvousPoint(server, RendezvousPoint.CreateRoom())
                : throw new InvalidOperationException(Texts.Get("Session.Error.BadRendezvousServer"));
        }

        var listener = new TlsPeerListener(_identity, new TlsTransportOptions { Port = port }, _loggerFactory.CreateLogger<TlsPeerListener>());
        try
        {
            listener.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _ = listener.DisposeAsync();
            throw new InvalidOperationException(Texts.Format("Session.Error.PortBusy", port, ex.Message), ex);
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
        host.RelayServer = _relay;
        host.Rendezvous = rendezvous;
        Attach(host);
        _host = host;
        host.Start();
        if (rendezvous is not null)
        {
            _rendezvousHost = StartRendezvous(host, listener, rendezvous);
        }
        if (_portMapper is not null && OpenRouterPort)
        {
            _ = OpenPortAsync(host, host.Port, _relay?.Port);
        }

        ReportFirewall();
    }

    /// <inheritdoc />
    public Task ShareMediaAsync(string path, CancellationToken cancellationToken)
        => (_host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostShares"))).ShareMediaAsync(path, cancellationToken);

    /// <inheritdoc />
    public Invite CreateInvite(IReadOnlyList<PeerEndpoint> additionalEndpoints, TimeSpan lifetime)
    {
        var host = _host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostInvites"));
        var mapping = _portLease?.Mapping;
        var external = mapping is { IsPubliclyReachable: true, ExternalAddress: { } address }
            ? [new PeerEndpoint(address.ToString(), mapping.ExternalPort)]
            : Array.Empty<PeerEndpoint>();
        var endpoints = EndpointDiscovery.Discover(host.Port, [.. additionalEndpoints, .. external]);
        if (endpoints.Count == 0)
        {
            // A computer without a network has nothing to put into an invitation.
            throw new InvalidOperationException(Texts.Get("Session.Error.NoAddresses"));
        }

        return host.CreateInvite(endpoints, lifetime);
    }

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
        => _host is not null ? _host.RequestAsync(request, cancellationToken)
            : _participant is not null ? _participant.RequestAsync(request, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
        => (_host ?? throw new InvalidOperationException(Texts.Get("Session.Error.OnlyHostModerates")))
            .SwitchOffParticipantDevicesAsync(peerId, microphone, camera);

    /// <inheritdoc />
    public Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
        => _host is not null ? _host.SendChatAsync(kind, text)
            : _participant is not null ? _participant.SendChatAsync(kind, text, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
        => _host is not null ? _host.SendDrawAsync(strokeId, phase, points)
            : _participant is not null ? _participant.SendDrawAsync(strokeId, phase, points, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task SetAvatarAsync(byte[]? image, CancellationToken cancellationToken)
        => _host is not null ? _host.SetAvatarAsync(image)
            : _participant is not null ? _participant.SendAvatarAsync(image, cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// A joined session that has ended and is not being restored has nothing to show any more: like
    /// <see cref="IsActive"/>, the snapshot is gone, so the view clears the participants, the chat and the strokes
    /// instead of carrying them into the next session.
    /// </remarks>
    public SessionSnapshot? GetSnapshot()
    {
        if (_host is { } host)
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
        var host = Interlocked.Exchange(ref _host, null);
        var participant = Interlocked.Exchange(ref _participant, null);

        // Every part is released on its own: one that fails must not leave the others behind (a session that keeps
        // its port, a mapping that stays on the router).
        await ReleaseAsync(Interlocked.Exchange(ref _portLease, null)).ConfigureAwait(false);
        await ReleaseAsync(Interlocked.Exchange(ref _relayLease, null)).ConfigureAwait(false);
        await ReleaseAsync(Interlocked.Exchange(ref _relay, null)).ConfigureAwait(false);
        await ReleaseAsync(Interlocked.Exchange(ref _rendezvousHost, null)).ConfigureAwait(false);
        await ReleaseAsync(host).ConfigureAwait(false);
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
            null when HasTunnelAddress() => Texts.Format("Session.Router.NotOpenedTunnelFound", port),
            null => Texts.Format("Session.Router.NotOpened", port),
            { IsPubliclyReachable: true } => Texts.Format("Session.Router.Opened", mapping.Method, mapping.ExternalAddress, mapping.ExternalPort),
            _ => Texts.Format("Session.Router.OpenedNotPublic", mapping.Method, mapping.ExternalAddress?.ToString() ?? Texts.Get("Session.Router.UnknownAddress")),
        };
        if (_host == host)
        {
            EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), null, text));
        }
    }

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
                .RunAsync(program, WindowsFirewall.BuildArguments(WindowsFirewall.AllowVerb), TimeSpan.FromMinutes(2), cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != WindowsFirewall.Success)
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
    /// Checks whether this device is on a tunnel network (AmneziaWG, WireGuard). Such an address reaches the other
    /// computers of that network whatever the router does, so the failed port mapping is not the end of the story.
    /// </summary>
    /// <returns><see langword="true"/> when a tunnel address was found.</returns>
    private static bool HasTunnelAddress()
    {
        try
        {
            return EndpointDiscovery.DiscoverTunnelAddresses().Count > 0;
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return false;
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
            TurnServer? relay = null;
            try
            {
                // Creating the relay opens a socket already, so it may fail like the start does.
                relay = new TurnServer(
                    new TurnServerOptions
                    {
                        Credentials = RelayCredentials.CreateRandom(port),
                        ListenAddress = System.Net.Sockets.Socket.OSSupportsIPv6 ? System.Net.IPAddress.IPv6Any : System.Net.IPAddress.Any,
                    },
                    _timeProvider,
                    _loggerFactory.CreateLogger<TurnServer>());
                relay.Start();
                return relay;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                _loggerFactory.CreateLogger<SessionService>().LogInformation("Relay port {Port} is busy: {Error}", port, ex.SocketErrorCode);
                if (relay is not null)
                {
                    _ = relay.DisposeAsync();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Takes the room of the hosted session on the rendezvous server. The connections punched to participants go to
    /// the listener of the session, which authenticates and admits them like any other.
    /// </summary>
    /// <param name="host">The hosted session.</param>
    /// <param name="listener">The listener of the session.</param>
    /// <param name="point">The server and the room.</param>
    /// <returns>The running agent.</returns>
    private RendezvousHost StartRendezvous(HostSession host, TlsPeerListener listener, RendezvousPoint point)
    {
        var agent = new RendezvousHost(
            point,
            new RendezvousOwner(_identity.PublicCertificateDer, data => _identity.Sign(data)),
            socket => listener.Adopt(socket),
            timeProvider: _timeProvider,
            logger: _loggerFactory.CreateLogger<RendezvousHost>());
        agent.StateChanged += (_, _) =>
        {
            if (_host != host)
            {
                return;
            }

            var text = agent.IsRegistered
                ? Texts.Format("Session.Rendezvous.Registered", point.Server)
                : agent.LastError is { } error ? Texts.Format("Session.Rendezvous.Lost", point.Server, RendezvousException.Describe(error)) : null;
            if (text is not null)
            {
                EventRaised?.Invoke(this, new SessionEvent(_timeProvider.GetLocalNow(), null, text));
            }
        };
        agent.Start();
        return agent;
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
        var token = cancellation.Token;
        try
        {
            for (var attempt = 1; attempt <= ReconnectAttempts && !token.IsCancellationRequested; attempt++)
            {
                var delay = ReconnectDelays[Math.Min(attempt - 1, ReconnectDelays.Length - 1)];
                Report(Texts.Format("Session.Reconnect.Lost", attempt, ReconnectAttempts, delay.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
                await Task.Delay(delay, _timeProvider, token).ConfigureAwait(false);
                if (!ReferenceEquals(_participant, ended) || _reconnectInvite is not { } ticket)
                {
                    return;
                }

                Report(Texts.Format("Session.Reconnect.Trying", attempt, ReconnectAttempts));

                // The ended session shares the player and the cameras with the new one. It is released before the
                // new one starts: released afterwards, its shutdown would stop the film and the cameras the new
                // session has just started.
                await ended.DisposeAsync().ConfigureAwait(false);
                var session = CreateParticipant(_joinName);
                try
                {
                    await session.RejoinAsync(ticket, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
                {
                    // Whatever went wrong, the half-open session is released: left behind, it would keep its
                    // connection and its loops for ever.
                    var refused = session.ClosedByHost;
                    await session.DisposeAsync().ConfigureAwait(false);
                    _loggerFactory.CreateLogger<SessionService>().LogInformation("Reconnect attempt {Attempt} failed: {Error}", attempt, ex.Message);
                    if (refused)
                    {
                        // The host answered and said no: asking again would only bother it.
                        break;
                    }

                    continue;
                }
                catch (OperationCanceledException)
                {
                    // The user left while the attempt was running: the half-open session must not stay behind.
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                if (!ReferenceEquals(Interlocked.CompareExchange(ref _participant, session, ended), ended))
                {
                    // The user left while the host was admitting this device again.
                    await session.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                _reconnectInvite = session.ReconnectInvite ?? ticket;
                Report(Texts.Get("Session.Reconnect.Restored"));
                ReconnectMessage = string.Empty;
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            Report(Texts.Get("Session.Reconnect.Failed"));
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
            StopwatchMonotonicClock.Instance,
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
        if (_reconnecting is null
            && _participant is { } ended
            && ended.GetSnapshot().State == SessionState.Ended
            && ReferenceEquals(Interlocked.CompareExchange(ref _participant, null, ended), ended))
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
        session.Changed += (_, _) =>
        {
            if (session.GetSnapshot().State == SessionState.Ended)
            {
                StartReconnecting(session);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        };
        session.EventRaised += (_, e) => EventRaised?.Invoke(this, e);
        session.ChatReceived += (_, e) => ChatReceived?.Invoke(this, e);
        session.DrawReceived += (_, e) => DrawReceived?.Invoke(this, e);
        session.AvatarReceived += (_, e) => AvatarReceived?.Invoke(this, e);
    }
}
