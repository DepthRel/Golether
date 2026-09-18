namespace Golether.Core.Playback;

/// <summary>
/// A part of the media timeline.
/// </summary>
/// <param name="Start">The start.</param>
/// <param name="End">The end.</param>
public readonly record struct MediaTimeRange(TimeSpan Start, TimeSpan End);

/// <summary>
/// A snapshot of the local player.
/// </summary>
/// <param name="IsLoaded">Whether a media file is loaded.</param>
/// <param name="Position">The current position, or <see langword="null"/> while it is unknown.</param>
/// <param name="Duration">The media duration, or <see langword="null"/> while it is unknown.</param>
/// <param name="IsPaused">Whether the player is paused.</param>
/// <param name="IsBuffering">Whether the player waits for data.</param>
/// <param name="CacheAhead">The amount of media buffered ahead of the position.</param>
/// <param name="Rate">The current playback rate (including drift correction).</param>
public readonly record struct PlayerSnapshot(
    bool IsLoaded,
    TimeSpan? Position,
    TimeSpan? Duration,
    bool IsPaused,
    bool IsBuffering,
    TimeSpan CacheAhead,
    double Rate)
{
    /// <summary>
    /// Gets the snapshot of a player without media.
    /// </summary>
    public static PlayerSnapshot Empty { get; } = new(false, null, null, true, false, TimeSpan.Zero, 1.0);

    /// <summary>
    /// Gets the parts of the media that can be played without waiting (the player's cache), or
    /// <see langword="null"/> when unknown.
    /// </summary>
    public IReadOnlyList<MediaTimeRange>? Buffered { get; init; }
}

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
