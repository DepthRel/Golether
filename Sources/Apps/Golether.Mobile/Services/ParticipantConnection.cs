using Microsoft.Extensions.Logging;
using Golether.Core.Data.Enums;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Conference;
using Golether.Media.Player.Mpv;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;
using Golether.Transports.Rendezvous;
using Golether.Transports.Tls;

namespace Golether.Mobile.Services;

/// <summary>
/// The place of this device in a session: the session of the moment and the way back to it. A phone changes networks,
/// and the first session is replaced by a new one after every such change.
/// </summary>
public sealed class ParticipantConnection : IParticipantConnection, IReconnectionClient
{
    /// <summary>
    /// How much of the film is kept in memory: a phone is closed by the system for using as much as a computer may,
    /// and the part read ahead of the picture (128 MiB) must still fit.
    /// </summary>
    private const long MEDIA_CACHE_CAPACITY = 256L * 1024 * 1024;

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
    /// The name shown to others.
    /// </summary>
    private readonly string _displayName;

    /// <summary>
    /// The session of the moment, <see langword="null"/> once the user left.
    /// </summary>
    private ParticipantSession? _session;

    /// <summary>
    /// The ticket to come back with, <see langword="null"/> until the host has let the device in.
    /// </summary>
    private Invite? _ticket;

    /// <summary>
    /// Stops the way back, <see langword="null"/> when the device is not on it.
    /// </summary>
    private CancellationTokenSource? _reconnecting;

    /// <summary>
    /// The reason of the end when the way back failed.
    /// </summary>
    private string? _failure;

    /// <summary>
    /// The attempts to come back that run now, <see langword="null"/> outside them.
    /// </summary>
    private ParticipantReconnector? _reconnector;

    /// <summary>
    /// The picture of the user, sent into every session this connection joins, or <see langword="null"/>.
    /// </summary>
    private byte[]? _avatar;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantConnection"/> class.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="conference">The cameras and voices.</param>
    /// <param name="current">What follows the session of the moment.</param>
    /// <param name="displayName">The name shown to others.</param>
    public ParticipantConnection(IMobileServices services, IConferenceMedia conference, CurrentSession current, string displayName)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _conference = conference ?? throw new ArgumentNullException(nameof(conference));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _displayName = displayName;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <inheritdoc />
    public bool IsReconnecting => _reconnecting is not null;

    /// <inheritdoc />
    public string ReconnectMessage { get; private set; } = string.Empty;

    /// <inheritdoc />
    public string? EndReason => _failure ?? _session?.EndReason;

    /// <inheritdoc />
    public async Task JoinAsync(Invite invite, CancellationToken cancellationToken)
    {
        var session = CreateSession();
        _session = session;
        _current.Attach(session);

        await session.JoinAsync(invite, cancellationToken);
        _ticket = session.ReconnectInvite;
        await SendAvatarAsync(session, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetAvatarAsync(byte[]? image)
    {
        _avatar = image;

        if (_session is { } session)
        {
            await SendAvatarAsync(session, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public SessionSnapshot? GetSnapshot() => _session?.GetSnapshot();

    /// <inheritdoc />
    public void RetryNow() => _reconnector?.Retry();

    /// <inheritdoc />
    public Task RequestAsync(PlaybackRequest request)
        => _session is { } session ? session.RequestAsync(request, CancellationToken.None) : Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var reconnecting = Interlocked.Exchange(ref _reconnecting, null);
        if (reconnecting is not null)
        {
            await reconnecting.CancelAsync();
        }

        _ticket = null;
        var session = Interlocked.Exchange(ref _session, null);
        _current.Detach();

        if (session is not null)
        {
            await session.DisposeAsync();
        }
    }

    /// <inheritdoc />
    Invite? IReconnectionClient.GetTicket(ParticipantSession ended) => ReferenceEquals(_session, ended) ? _ticket : null;

    /// <inheritdoc />
    async Task<bool> IReconnectionClient.AdoptAsync(ParticipantSession ended, ParticipantSession session, Invite ticket, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _session, session, ended), ended))
        {
            return false;
        }

        _ticket = session.ReconnectInvite ?? ticket;
        _current.Attach(session);
        await SendAvatarAsync(session, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    void IReconnectionClient.Report(string text)
    {
        ReconnectMessage = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public ParticipantSession CreateSession()
    {
        var connector = new TlsPeerConnector(_services.Identity, new TlsTransportOptions());
        var session = new ParticipantSession(
            _services.Identity,
            connector,
            _services.Playback,
            new MpvMediaUriRegistry(),
            SystemMonotonicClock.Instance,
            TimeProvider.System,
            _displayName,
            new SessionOptions { MediaCacheCapacity = MEDIA_CACHE_CAPACITY },
            _services.LoggerFactory,
            _conference,
            new RendezvousPeerConnector(connector, new RendezvousDialer()));

        session.Changed += (_, _) => OnSessionChanged(session);
        session.AvatarReceived += (_, update) => AvatarReceived?.Invoke(this, update);
        return session;
    }

    /// <summary>
    /// Sends the picture of the user into a session; a broken connection is no failure, the way back sends it again.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Stops sending.</param>
    /// <returns>A task that completes when the picture is sent.</returns>
    private async Task SendAvatarAsync(ParticipantSession session, CancellationToken cancellationToken)
    {
        if (_avatar is not { Length: > 0 } avatar)
        {
            return;
        }

        try
        {
            await session.SendAvatarAsync(avatar, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            // The connection broke: the way back sends the picture again.
        }
    }

    /// <summary>
    /// Starts the way back when the connection of the session of the moment broke.
    /// </summary>
    /// <param name="session">The session that changed.</param>
    private void OnSessionChanged(ParticipantSession session)
    {
        if (session.GetSnapshot().State == SessionState.Ended)
        {
            StartReconnecting(session);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts coming back to the session. Only a broken connection is restored: when the host ended the session or
    /// removed this device, knocking again would only bother the host.
    /// </summary>
    /// <param name="session">The session that ended.</param>
    private void StartReconnecting(ParticipantSession session)
    {
        if (!ReferenceEquals(_session, session) || session.ClosedByHost || _ticket is null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _reconnecting, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return;
        }

        _ = ReconnectAsync(session, cancellation);
    }

    /// <summary>
    /// Goes back to the session and remembers why it ended if that did not work.
    /// </summary>
    /// <param name="ended">The session that ended.</param>
    /// <param name="cancellation">Stops the way back.</param>
    /// <returns>A task that completes when the session is back or the attempts stop.</returns>
    private async Task ReconnectAsync(ParticipantSession ended, CancellationTokenSource cancellation)
    {
        try
        {
            var logger = _services.LoggerFactory.CreateLogger<ParticipantReconnector>();
            var reconnector = new ParticipantReconnector(this, TimeProvider.System, logger);
            _reconnector = reconnector;
            var restored = await reconnector.RunAsync(ended, cancellation.Token);
            _failure = restored ? null : Texts.Get("Session.Reconnect.Failed");
        }
        catch (OperationCanceledException)
        {
            // The user left, and the view has already gone on.
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            _failure = ex.Message;
        }
        finally
        {
            _reconnector = null;
            Interlocked.CompareExchange(ref _reconnecting, null, cancellation);
            ReconnectMessage = string.Empty;
            cancellation.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
