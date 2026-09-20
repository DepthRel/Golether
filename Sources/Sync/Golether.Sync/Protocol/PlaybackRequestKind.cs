namespace Golether.Sync.Protocol;

/// <summary>
/// The kind of a playback request.
/// </summary>
public enum PlaybackRequestKind
{
    /// <summary>
    /// Start or resume playback.
    /// </summary>
    Play = 0,

    /// <summary>
    /// Pause playback.
    /// </summary>
    Pause = 1,

    /// <summary>
    /// Change the position.
    /// </summary>
    Seek = 2,

    /// <summary>
    /// Start playback at once, without waiting for participants that are not ready.
    /// </summary>
    PlayNow = 3,
}
