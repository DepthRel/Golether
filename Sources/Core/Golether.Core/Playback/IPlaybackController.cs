namespace Golether.Core.Playback;

/// <summary>
/// Controls the local media player.
/// </summary>
/// <remarks>
/// The synchronization layer drives the player only through this interface. User intents (play, pause, seek) come from
/// the UI commands, never from player events, so a change applied on behalf of another participant is never broadcast
/// again.
/// </remarks>
public interface IPlaybackController : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the player backend is available (for example, whether libmpv was found).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Returns the current state of the player.
    /// </summary>
    /// <returns>The snapshot.</returns>
    PlayerSnapshot GetSnapshot();

    /// <summary>
    /// Loads a media source.
    /// </summary>
    /// <param name="source">The media URI (<c>file://</c> or <c>golether://</c>).</param>
    /// <param name="startPosition">The initial position.</param>
    /// <param name="paused">Whether to stay paused after loading.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the load command was accepted.</returns>
    Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses or resumes playback.
    /// </summary>
    /// <param name="paused">Whether to pause.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the command was accepted.</returns>
    Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeks precisely to a position.
    /// </summary>
    /// <param name="position">The target position.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the command was accepted.</returns>
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the playback rate; audio pitch is preserved.
    /// </summary>
    /// <param name="rate">The rate, for example <c>1.03</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the command was accepted.</returns>
    Task SetRateAsync(double rate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops playback and unloads the media.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the command was accepted.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
