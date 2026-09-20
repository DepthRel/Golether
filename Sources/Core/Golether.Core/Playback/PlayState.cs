namespace Golether.Core.Playback;

/// <summary>
/// Playback mode of the session.
/// </summary>
public enum PlayState
{
    /// <summary>
    /// Playback is paused at <see cref="PlaybackState.Position"/>.
    /// </summary>
    Paused = 0,

    /// <summary>
    /// Playback runs (or is scheduled to start) from <see cref="PlaybackState.Position"/> at
    /// <see cref="PlaybackState.ReferenceTime"/>.
    /// </summary>
    Playing = 1,
}
