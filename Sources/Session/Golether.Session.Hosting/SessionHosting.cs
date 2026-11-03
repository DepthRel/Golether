using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Golether.Core.Networking;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Security.Admission;
using Golether.Security.Invites;
using Golether.Transports.PortMapping;
using Golether.Transports.Relay;
using Golether.Transports.Rendezvous;
using Golether.Transports.Tls;

namespace Golether.Session.Hosting;

/// <summary>
/// A hosted session on its network: the <see cref="HostSession"/> with the listener of its port, the TURN relay of the
/// cameras and voices, the room on the rendezvous server and the ports opened on the router. Created first, so the
/// application can follow the events of the session before it starts; then started; disposed to end the session and
/// give everything back.
/// </summary>
public sealed class SessionHosting : IAsyncDisposable
{
    /// <summary>
    /// What the session is made of.
    /// </summary>
    private readonly HostingSetup _setup;

    /// <summary>
    /// The listener of the session.
    /// </summary>
    private readonly TlsPeerListener _listener;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<SessionHosting> _logger;

    /// <summary>
    /// The router mapping of the session port.
    /// </summary>
    private PortMappingLease? _portLease;

    /// <summary>
    /// The router mapping of the relay port.
    /// </summary>
    private PortMappingLease? _relayLease;

    /// <summary>
    /// The TURN relay of the session, or <see langword="null"/> when no port could be used.
    /// </summary>
    private TurnServer? _relay;

    /// <summary>
    /// Holds the room of the session on the rendezvous server, or <see langword="null"/>.
    /// </summary>
    private RendezvousHost? _rendezvousHost;

    /// <summary>
    /// Whether the session was ended (1) or not (0).
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionHosting"/> class.
    /// </summary>
    /// <param name="setup">What the session is made of.</param>
    /// <param name="listener">The listening listener.</param>
    /// <param name="session">The session, not started yet.</param>
    /// <param name="relay">The relay, or <see langword="null"/>.</param>
    private SessionHosting(HostingSetup setup, TlsPeerListener listener, HostSession session, TurnServer? relay)
    {
        _setup = setup;
        _listener = listener;
        _relay = relay;
        _logger = setup.LoggerFactory.CreateLogger<SessionHosting>();
        Session = session;
    }

    /// <summary>
    /// Raised for the event feed when the router or the rendezvous server answered. Raised on a background thread.
    /// </summary>
    public event EventHandler<SessionEvent>? EventRaised;

    /// <summary>
    /// Gets the hosted session.
    /// </summary>
    public HostSession Session { get; }

    /// <summary>
    /// Listens on the port and prepares the session and its relay; nobody is let in before <see cref="Start"/>.
    /// </summary>
    /// <param name="setup">What the session is made of.</param>
    /// <returns>The hosting.</returns>
    /// <exception cref="InvalidOperationException">The port is busy; the message says so in the words of the user.</exception>
    public static SessionHosting Create(HostingSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var listener = new TlsPeerListener(setup.Identity, new TlsTransportOptions { Port = setup.Port }, setup.LoggerFactory.CreateLogger<TlsPeerListener>());
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            _ = listener.DisposeAsync();
            throw new InvalidOperationException(Texts.Format("Session.Error.PortBusy", setup.Port, ex.Message), ex);
        }

        var session = new HostSession(
            setup.Identity,
            listener,
            new InviteRegistry(setup.TimeProvider),
            new AdmissionService(setup.Prompt, new AdmissionOptions(), setup.TimeProvider),
            setup.Contacts,
            setup.Player,
            SystemMonotonicClock.Instance,
            setup.TimeProvider,
            setup.SessionName,
            setup.DisplayName,
            setup.Options,
            setup.LoggerFactory,
            setup.Conference,
            setup.Thumbnails,
            setup.Frames,
            setup.GalleryDirectory);
        var relay = StartRelay(listener.Port, setup);
        session.Relay = relay?.Credentials;
        session.RelayServer = relay;
        session.Rendezvous = setup.Rendezvous;

        return new SessionHosting(setup, listener, session, relay);
    }

    /// <summary>
    /// Starts the session: participants may knock, the room on the rendezvous server is taken and the router is asked
    /// to open the ports.
    /// </summary>
    public void Start()
    {
        Session.Start();
        if (_setup.Rendezvous is { } rendezvous)
        {
            _rendezvousHost = StartRendezvous(rendezvous);
        }

        if (_setup.PortMapper is { } mapper)
        {
            _ = OpenPortAsync(mapper, Session.Port, _relay?.Port);
        }
    }

    /// <summary>
    /// Creates an invitation with the addresses of this device, those entered by the user first and the public address
    /// the router gave last.
    /// </summary>
    /// <param name="additionalEndpoints">Addresses entered by the user, listed first.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="InvalidOperationException">This device has no network to put into an invitation.</exception>
    public Invite CreateInvite(IReadOnlyList<PeerEndpoint> additionalEndpoints, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(additionalEndpoints);

        var mapping = _portLease?.Mapping;
        var external = mapping is { IsPubliclyReachable: true, ExternalAddress: { } address }
            ? [new PeerEndpoint(address.ToString(), mapping.ExternalPort)]
            : Array.Empty<PeerEndpoint>();
        var endpoints = EndpointDiscovery.Discover(Session.Port, [.. additionalEndpoints, .. external]);
        if (endpoints.Count == 0)
        {
            // A device without a network has nothing to put into an invitation.
            throw new InvalidOperationException(Texts.Get("Session.Error.NoAddresses"));
        }

        return Session.CreateInvite(endpoints, lifetime);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every part is released on its own: one that fails must not leave the others behind (a session that keeps its
    /// port, a mapping that stays on the router).
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await ReleaseAsync(Interlocked.Exchange(ref _portLease, null)).ConfigureAwait(false);
        await ReleaseAsync(Interlocked.Exchange(ref _relayLease, null)).ConfigureAwait(false);
        await ReleaseAsync(Interlocked.Exchange(ref _relay, null)).ConfigureAwait(false);
        await ReleaseAsync(Interlocked.Exchange(ref _rendezvousHost, null)).ConfigureAwait(false);
        await ReleaseAsync(Session).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the TURN relay for cameras and voices on the port after the session port, or any free port.
    /// </summary>
    /// <param name="sessionPort">The session port.</param>
    /// <param name="setup">What the session is made of.</param>
    /// <returns>The relay, or <see langword="null"/> when no port could be used.</returns>
    private static TurnServer? StartRelay(int sessionPort, HostingSetup setup)
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
                        ListenAddress = Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any,
                    },
                    setup.TimeProvider,
                    setup.LoggerFactory.CreateLogger<TurnServer>());
                relay.Start();
                return relay;
            }
            catch (SocketException ex)
            {
                setup.LoggerFactory.CreateLogger<SessionHosting>().LogInformation("Relay port {Port} is busy: {Error}", port, ex.SocketErrorCode);
                if (relay is not null)
                {
                    _ = relay.DisposeAsync();
                }
            }
        }

        return null;
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
    /// Takes the room of the session on the rendezvous server. The connections punched to participants go to the
    /// listener of the session, which authenticates and admits them like any other.
    /// </summary>
    /// <param name="point">The server and the room.</param>
    /// <returns>The running agent.</returns>
    private RendezvousHost StartRendezvous(RendezvousPoint point)
    {
        var identity = _setup.Identity;
        var agent = new RendezvousHost(
            point,
            new RendezvousOwner(identity.PublicCertificateDer, data => identity.Sign(data)),
            socket => _listener.Adopt(socket),
            timeProvider: _setup.TimeProvider,
            logger: _setup.LoggerFactory.CreateLogger<RendezvousHost>());
        agent.StateChanged += (_, _) =>
        {
            var text = agent.IsRegistered
                ? Texts.Get("Session.Rendezvous.Registered")
                : agent.LastError is { } error ? Texts.Format("Session.Rendezvous.Lost", RendezvousException.Describe(error)) : null;
            if (text is not null)
            {
                Raise(text);
            }
        };
        agent.Start();
        return agent;
    }

    /// <summary>
    /// Opens the session port and the relay port on the router and reports the result in the event feed.
    /// </summary>
    /// <param name="mapper">Opens the ports on the router.</param>
    /// <param name="port">The session port.</param>
    /// <param name="relayPort">The port of the TURN relay, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the router answered or gave up.</returns>
    private async Task OpenPortAsync(PortMapper mapper, int port, int? relayPort)
    {
        if (relayPort is { } relay)
        {
            var relayLease = await TryMapAsync(mapper, relay).ConfigureAwait(false);
            if (relayLease is not null && (IsDisposed || Interlocked.CompareExchange(ref _relayLease, relayLease, null) is not null))
            {
                await relayLease.DisposeAsync().ConfigureAwait(false);
            }
            else if (relayLease is { Mapping.ExternalPort: var external } && external != relay)
            {
                _logger.LogWarning("The router moved the relay port {Port} to {External}; relayed cameras from other networks will not connect", relay, external);
            }
        }

        PortMappingLease? lease;
        try
        {
            lease = await mapper.MapAsync(port, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lease = null;
            _logger.LogDebug(ex, "Port {Port} was not opened", port);
        }

        if (lease is not null && (IsDisposed || Interlocked.CompareExchange(ref _portLease, lease, null) is not null))
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
        Raise(text);
    }

    /// <summary>
    /// Asks the router to open a port.
    /// </summary>
    /// <param name="mapper">Opens the ports on the router.</param>
    /// <param name="port">The port.</param>
    /// <returns>The lease, or <see langword="null"/>.</returns>
    private async Task<PortMappingLease?> TryMapAsync(PortMapper mapper, int port)
    {
        try
        {
            return await mapper.MapAsync(port, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Port {Port} was not opened", port);
            return null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the session has ended.
    /// </summary>
    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Puts a line into the event feed, unless the session has ended.
    /// </summary>
    /// <param name="text">The text.</param>
    private void Raise(string text)
    {
        if (!IsDisposed)
        {
            EventRaised?.Invoke(this, new SessionEvent(_setup.TimeProvider.GetLocalNow(), null, text));
        }
    }

    /// <summary>
    /// Releases a part of the session, logging a failure instead of passing it on.
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
            _logger.LogWarning(ex, "Releasing {Part} failed", part.GetType().Name);
        }
    }
}
