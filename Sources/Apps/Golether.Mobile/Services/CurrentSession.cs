using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Localization;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.Services;

/// <summary>
/// Connects the chat, the pen and the shown pictures, which live as long as the application, to the session of the
/// moment: one this device joined, or one it hosts.
/// </summary>
public sealed class CurrentSession : ISessionMessages, IGallerySession
{
    /// <summary>
    /// The joined session, or <see langword="null"/>.
    /// </summary>
    private ParticipantSession? _session;

    /// <summary>
    /// The hosted session, or <see langword="null"/>.
    /// </summary>
    private HostSession? _host;

    /// <inheritdoc />
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <inheritdoc />
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Follows a new joined session and leaves the previous session.
    /// </summary>
    /// <param name="session">The session.</param>
    public void Attach(ParticipantSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Detach();

        _session = session;
        session.ChatReceived += OnChatReceived;
        session.DrawReceived += OnDrawReceived;
    }

    /// <summary>
    /// Follows a new hosted session and leaves the previous session.
    /// </summary>
    /// <param name="host">The session.</param>
    public void Attach(HostSession host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Detach();

        _host = host;
        host.ChatReceived += OnChatReceived;
        host.DrawReceived += OnDrawReceived;
    }

    /// <inheritdoc />
    public Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
        => _host is { } host ? host.SendChatAsync(kind, text) : Session.SendChatAsync(kind, text, cancellationToken);

    /// <inheritdoc />
    public Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
        => _host is { } host ? host.SendDrawAsync(strokeId, phase, points) : Session.SendDrawAsync(strokeId, phase, points, cancellationToken);

    /// <inheritdoc />
    public Task CloseGalleryAsync()
        => _host is { } host ? host.CloseGalleryAsync() : Task.FromException(new InvalidOperationException(Texts.Get("Session.Error.OnlyHostShares")));

    /// <inheritdoc />
    public Task ShowItemAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        if (_host is { } host)
        {
            return host.ShowItemAsync(catalogId, index, cancellationToken);
        }

        return _session is { } session ? session.ShowItemAsync(catalogId, index, cancellationToken) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetGalleryView(int? index)
    {
        _host?.SetGalleryView(index);
        _session?.SetGalleryView(index);
    }

    /// <inheritdoc />
    public Task<byte[]?> LoadThumbnailAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        if (_host is { } host)
        {
            return host.LoadThumbnailAsync(catalogId, index, cancellationToken);
        }

        return _session is { } session ? session.LoadThumbnailAsync(catalogId, index, cancellationToken) : Task.FromResult<byte[]?>(null);
    }

    /// <inheritdoc />
    public Task<byte[]> LoadPictureAsync(string catalogId, int index, CancellationToken cancellationToken)
    {
        if (_host is { } host)
        {
            return host.LoadPictureAsync(catalogId, index, cancellationToken);
        }

        return _session is { } session
            ? session.LoadPictureAsync(catalogId, index, cancellationToken)
            : Task.FromException<byte[]>(new InvalidOperationException(Texts.Get("Session.Error.NotJoined")));
    }

    /// <summary>
    /// Gets the joined session, which must be there.
    /// </summary>
    private ParticipantSession Session => _session ?? throw new InvalidOperationException(Texts.Get("Chat.NotSent"));

    /// <summary>
    /// Stops following the session of the moment: it ended.
    /// </summary>
    public void Detach()
    {
        if (_session is { } previous)
        {
            previous.ChatReceived -= OnChatReceived;
            previous.DrawReceived -= OnDrawReceived;
        }

        if (_host is { } host)
        {
            host.ChatReceived -= OnChatReceived;
            host.DrawReceived -= OnDrawReceived;
        }

        _session = null;
        _host = null;
    }

    /// <summary>
    /// Passes a chat line on.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="entry">The line.</param>
    private void OnChatReceived(object? sender, ChatEntry entry) => ChatReceived?.Invoke(this, entry);

    /// <summary>
    /// Passes a stroke on.
    /// </summary>
    /// <param name="sender">The session.</param>
    /// <param name="update">The stroke.</param>
    private void OnDrawReceived(object? sender, StrokeUpdate update) => DrawReceived?.Invoke(this, update);
}
