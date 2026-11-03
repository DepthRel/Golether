using Golether.Session;
using Golether.Sync.Protocol;

namespace Golether.Mobile.Services;

/// <summary>
/// The place of this device in a session, as the screen sees it, whether the device joined the session or hosts it.
/// </summary>
public interface ISessionConnection : IAsyncDisposable
{
    /// <summary>
    /// Raised on any thread when anything about the session changes.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised on any thread when a participant chose a picture.
    /// </summary>
    event EventHandler<AvatarUpdate>? AvatarReceived;

    /// <summary>
    /// Gets a value indicating whether the device is on its way back to the session.
    /// </summary>
    bool IsReconnecting { get; }

    /// <summary>
    /// Gets how the way back goes, empty outside it.
    /// </summary>
    string ReconnectMessage { get; }

    /// <summary>
    /// Gets the reason why the session ended, when there is one to tell.
    /// </summary>
    string? EndReason { get; }

    /// <summary>
    /// Reads the state of the session.
    /// </summary>
    /// <returns>The state, or <see langword="null"/> once the user left.</returns>
    SessionSnapshot? GetSnapshot();

    /// <summary>
    /// Sends a request of the user to the session.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    Task RequestAsync(PlaybackRequest request);

    /// <summary>
    /// Remembers the picture of the user and sends it into the session, if there is one yet; it goes in again after
    /// joining and after every way back.
    /// </summary>
    /// <param name="image">The round picture as PNG, or <see langword="null"/> for none.</param>
    /// <returns>A task that completes when the picture is sent.</returns>
    Task SetAvatarAsync(byte[]? image);

    /// <summary>
    /// Starts the next attempt to come back without waiting any longer: the network is here again.
    /// </summary>
    void RetryNow();
}
