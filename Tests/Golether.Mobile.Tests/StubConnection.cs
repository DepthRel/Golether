using Golether.Mobile.Services;
using Golether.Security.Invites;
using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.Tests;

/// <summary>
/// The place in a session as a test wants it, without a network.
/// </summary>
internal sealed class StubConnection : IParticipantConnection
{
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <summary>
    /// Gets or sets the state of the session.
    /// </summary>
    public SessionSnapshot? Snapshot { get; set; }

    /// <inheritdoc />
    public bool IsReconnecting { get; set; }

    /// <inheritdoc />
    public string ReconnectMessage { get; set; } = string.Empty;

    /// <inheritdoc />
    public string? EndReason { get; set; }

    /// <summary>
    /// Gets how many times the way back was asked to hurry.
    /// </summary>
    public int Retries { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the connection was closed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the requests the user made.
    /// </summary>
    public List<PlaybackRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Task JoinAsync(Invite invite, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public SessionSnapshot? GetSnapshot() => Snapshot;

    /// <inheritdoc />
    public Task RequestAsync(PlaybackRequest request)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RetryNow() => Retries++;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Tells that the session changed.
    /// </summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Delivers the picture of a participant.
    /// </summary>
    /// <param name="update">The picture.</param>
    public void RaiseAvatar(AvatarUpdate update) => AvatarReceived?.Invoke(this, update);
}
