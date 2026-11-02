using Golether.Client.Services;
using Golether.Core.Data.Enums;
using Golether.Localization;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.Services;

/// <summary>
/// Connects the chat, the pen and the shown pictures, which live as long as the application, to the session of the moment.
/// </summary>
public sealed class CurrentSession : ISessionMessages, IGallerySession
{
    /// <summary>
    /// The session, or <see langword="null"/> outside one.
    /// </summary>
    private ParticipantSession? _session;

    /// <inheritdoc />
    public event EventHandler<ChatEntry>? ChatReceived;

    /// <inheritdoc />
    public event EventHandler<StrokeUpdate>? DrawReceived;

    /// <summary>
    /// Follows a new session and leaves the previous one.
    /// </summary>
    /// <param name="session">The session, or <see langword="null"/> when it ended.</param>
    public void Attach(ParticipantSession? session)
    {
        if (_session is { } previous)
        {
            previous.ChatReceived -= OnChatReceived;
            previous.DrawReceived -= OnDrawReceived;
        }

        _session = session;

        if (session is not null)
        {
            session.ChatReceived += OnChatReceived;
            session.DrawReceived += OnDrawReceived;
        }
    }

    /// <inheritdoc />
    public Task SendChatAsync(ChatKind kind, string text, CancellationToken cancellationToken)
        => Session.SendChatAsync(kind, text, cancellationToken);

    /// <inheritdoc />
    public Task SendDrawAsync(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points, CancellationToken cancellationToken)
        => Session.SendDrawAsync(strokeId, phase, points, cancellationToken);

    /// <inheritdoc />
    public Task CloseGalleryAsync() => Task.FromException(new InvalidOperationException(Texts.Get("Session.Error.OnlyHostShares")));

    /// <inheritdoc />
    public Task ShowItemAsync(string catalogId, int index, CancellationToken cancellationToken)
        => _session is { } session ? session.ShowItemAsync(catalogId, index, cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public void SetGalleryView(int? index) => _session?.SetGalleryView(index);

    /// <inheritdoc />
    public Task<byte[]?> LoadThumbnailAsync(string catalogId, int index, CancellationToken cancellationToken)
        => _session is { } session ? session.LoadThumbnailAsync(catalogId, index, cancellationToken) : Task.FromResult<byte[]?>(null);

    /// <inheritdoc />
    public Task<byte[]> LoadPictureAsync(string catalogId, int index, CancellationToken cancellationToken)
        => _session is { } session
            ? session.LoadPictureAsync(catalogId, index, cancellationToken)
            : Task.FromException<byte[]>(new InvalidOperationException(Texts.Get("Session.Error.NotJoined")));

    /// <summary>
    /// Gets the session, which must be there.
    /// </summary>
    private ParticipantSession Session => _session ?? throw new InvalidOperationException(Texts.Get("Chat.NotSent"));

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
