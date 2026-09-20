namespace Golether.Core.Playback;

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
