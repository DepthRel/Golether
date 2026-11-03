using Microsoft.Extensions.Logging;
using Golether.Client.Services;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Media.Conference;
using Golether.Media.Streaming.Files;
using Golether.Media.Streaming.Gallery;
using Golether.Security.Admission;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Session.Hosting;
using Golether.Sync.Protocol;
using Golether.Transports.PortMapping;
using Golether.Transports.Tls;

namespace Golether.Mobile.Services;

/// <summary>
/// A session this phone hosts. A phone serves the film over its own network, often a mobile one, so the session is
/// smaller than on a computer, and the rendezvous server of the project is always looked for: a phone seldom has an
/// open port.
/// </summary>
public sealed class HostConnection : IHostConnection
{
    /// <summary>
    /// The most people in a session a phone hosts, the host included: every guest takes the film from the phone.
    /// </summary>
    public const int MAX_PARTICIPANTS = 3;

    /// <summary>
    /// The services.
    /// </summary>
    private readonly IMobileServices _services;

    /// <summary>
    /// The cameras and voices.
    /// </summary>
    private readonly IConferenceMedia _conference;

    /// <summary>
    /// What follows the session of the moment: the chat, the pen and the shown pictures.
    /// </summary>
    private readonly CurrentSession _current;

    /// <summary>
    /// Asks the user whether a participant may come in.
    /// </summary>
    private readonly IAdmissionPrompt _prompt;

    /// <summary>
    /// Finds the rendezvous server of the project, or <see langword="null"/>.
    /// </summary>
    private readonly IRendezvousDirectory? _rendezvous;

    /// <summary>
    /// Opens the ports on the router of a home network, or <see langword="null"/>.
    /// </summary>
    private readonly PortMapper? _portMapper;

    /// <summary>
    /// The contacts the participants are remembered in, or <see langword="null"/>.
    /// </summary>
    private readonly IContactStore? _contacts;

    /// <summary>
    /// The folder where a shown folder keeps the JPEG files the host makes.
    /// </summary>
    private readonly string _galleryDirectory;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<HostConnection> _logger;

    /// <summary>
    /// The running session, <see langword="null"/> before the start and once the user left.
    /// </summary>
    private SessionHosting? _hosting;

    /// <summary>
    /// The picture of the user, given before the start too, or <see langword="null"/>.
    /// </summary>
    private byte[]? _avatar;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostConnection"/> class.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="conference">The cameras and voices.</param>
    /// <param name="current">What follows the session of the moment.</param>
    /// <param name="prompt">Asks the user whether a participant may come in.</param>
    /// <param name="rendezvous">Finds the rendezvous server of the project, or <see langword="null"/>.</param>
    /// <param name="portMapper">Opens the ports on the router, or <see langword="null"/>.</param>
    /// <param name="contacts">The contacts, or <see langword="null"/>.</param>
    /// <param name="galleryDirectory">The folder of the JPEG files of a shown folder.</param>
    public HostConnection(
        IMobileServices services,
        IConferenceMedia conference,
        CurrentSession current,
        IAdmissionPrompt prompt,
        IRendezvousDirectory? rendezvous,
        PortMapper? portMapper,
        IContactStore? contacts,
        string galleryDirectory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _conference = conference ?? throw new ArgumentNullException(nameof(conference));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _rendezvous = rendezvous;
        _portMapper = portMapper;
        _contacts = contacts;
        _galleryDirectory = galleryDirectory;
        _logger = services.LoggerFactory.CreateLogger<HostConnection>();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <inheritdoc />
    public bool IsReconnecting => false;

    /// <inheritdoc />
    public string ReconnectMessage => string.Empty;

    /// <inheritdoc />
    public string? EndReason => null;

    /// <summary>
    /// Gets the running session, which must be there.
    /// </summary>
    private HostSession Session => _hosting?.Session ?? throw new InvalidOperationException("The session has not started.");

    /// <inheritdoc />
    public async Task StartAsync(string sessionName, string displayName, CancellationToken cancellationToken)
    {
        if (_hosting is not null)
        {
            throw new InvalidOperationException("The session has already started.");
        }

        var server = await FindServerAsync(cancellationToken);

        // A fresh room for every session: an old invitation does not lead to a new session.
        var rendezvous = server is { } found ? new RendezvousPoint(found, RendezvousPoint.CreateRoom()) : null;

        var hosting = Create(sessionName, displayName, rendezvous);
        hosting.Session.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        hosting.Session.AvatarReceived += (_, update) => AvatarReceived?.Invoke(this, update);
        hosting.EventRaised += (_, e) => _logger.LogInformation("{Event}", e.Text);
        _current.Attach(hosting.Session);
        _hosting = hosting;

        hosting.Start();
        if (_avatar is { Length: > 0 } avatar)
        {
            await hosting.Session.SetAvatarAsync(avatar);
        }
    }

    /// <inheritdoc />
    public async Task ShareMediaAsync(string path, CancellationToken cancellationToken)
        => await Session.ShareMediaAsync(path, cancellationToken);

    /// <inheritdoc />
    public Task ShowGalleryAsync(GalleryFolder folder, bool stripMetadata, CancellationToken cancellationToken)
        => Session.ShowGalleryAsync(folder, stripMetadata, null, cancellationToken);

    /// <inheritdoc />
    public Invite CreateInvite(TimeSpan lifetime)
        => (_hosting ?? throw new InvalidOperationException("The session has not started.")).CreateInvite([], lifetime);

    /// <inheritdoc />
    public Task SwitchOffParticipantDevicesAsync(PeerId peerId, bool microphone, bool camera)
        => Session.SwitchOffParticipantDevicesAsync(peerId, microphone, camera);

    /// <inheritdoc />
    public Task RemoveParticipantAsync(PeerId peerId) => Session.RemoveParticipantAsync(peerId);

    /// <inheritdoc />
    public SessionSnapshot? GetSnapshot() => _hosting?.Session.GetSnapshot();

    /// <inheritdoc />
    public Task RequestAsync(PlaybackRequest request)
        => _hosting is { } hosting ? hosting.Session.RequestAsync(request, CancellationToken.None) : Task.CompletedTask;

    /// <inheritdoc />
    public Task SetAvatarAsync(byte[]? image)
    {
        _avatar = image;

        return _hosting is { } hosting ? hosting.Session.SetAvatarAsync(image) : Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>The host has no way back to take: its session is where the participants come back to.</remarks>
    public void RetryNow()
    {
    }

    /// <inheritdoc />
    /// <remarks>The files chosen for the session are forgotten with it.</remarks>
    public async ValueTask DisposeAsync()
    {
        var hosting = Interlocked.Exchange(ref _hosting, null);
        _current.Detach();

        if (hosting is not null)
        {
            await hosting.DisposeAsync();
        }

        OpenedFiles.Clear();
    }

    /// <summary>
    /// Asks for the rendezvous server of the project; without it the session still works for the guests of the same
    /// network.
    /// </summary>
    /// <param name="cancellationToken">Stops asking.</param>
    /// <returns>The server, or <see langword="null"/>.</returns>
    private async Task<PeerEndpoint?> FindServerAsync(CancellationToken cancellationToken)
    {
        if (_rendezvous is not { } directory)
        {
            return null;
        }

        try
        {
            return await directory.FindAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            _logger.LogInformation(ex, "The rendezvous server was not found");
            return null;
        }
    }

    /// <summary>
    /// Creates the session on the usual port, or on any free port when another application holds it.
    /// </summary>
    /// <param name="sessionName">The name of the session.</param>
    /// <param name="displayName">The name of the host.</param>
    /// <param name="rendezvous">The rendezvous server and the room, or <see langword="null"/>.</param>
    /// <returns>The session, not started yet.</returns>
    private SessionHosting Create(string sessionName, string displayName, RendezvousPoint? rendezvous)
    {
        try
        {
            return SessionHosting.Create(Setup(sessionName, displayName, rendezvous, TlsTransportOptions.DEFAULT_PORT));
        }
        catch (InvalidOperationException ex)
        {
            // The port of the invitation is read from the session, so any port does.
            _logger.LogInformation(ex, "The usual port is busy");
            return SessionHosting.Create(Setup(sessionName, displayName, rendezvous, 0));
        }
    }

    /// <summary>
    /// Describes the session to create.
    /// </summary>
    /// <param name="sessionName">The name of the session.</param>
    /// <param name="displayName">The name of the host.</param>
    /// <param name="rendezvous">The rendezvous server and the room, or <see langword="null"/>.</param>
    /// <param name="port">The port.</param>
    /// <returns>The setup.</returns>
    private HostingSetup Setup(string sessionName, string displayName, RendezvousPoint? rendezvous, int port) => new()
    {
        Identity = _services.Identity,
        Prompt = _prompt,
        Contacts = _contacts,
        Player = _services.Playback,
        TimeProvider = TimeProvider.System,
        LoggerFactory = _services.LoggerFactory,
        Conference = _conference,
        SessionName = sessionName,
        DisplayName = displayName,
        Port = port,
        Options = new SessionOptions { MaxParticipants = MAX_PARTICIPANTS },
        Thumbnails = new SkiaPictureCodec(),
        GalleryDirectory = _galleryDirectory,
        Rendezvous = rendezvous,
        PortMapper = _portMapper,
    };
}
