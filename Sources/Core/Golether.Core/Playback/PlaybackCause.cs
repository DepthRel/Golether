namespace Golether.Core.Playback;

/// <summary>
/// The reason of a playback state change, shown in the event feed.
/// </summary>
public enum PlaybackCause
{
    /// <summary>
    /// The initial state of a session.
    /// </summary>
    Initial = 0,

    /// <summary>
    /// A participant started playback.
    /// </summary>
    Play = 1,

    /// <summary>
    /// A participant paused playback.
    /// </summary>
    Pause = 2,

    /// <summary>
    /// A participant changed the position.
    /// </summary>
    Seek = 3,

    /// <summary>
    /// The host paused playback until lagging participants have buffered enough data.
    /// </summary>
    WaitingForParticipants = 4,

    /// <summary>
    /// The host resumed playback after all participants had buffered enough data.
    /// </summary>
    ParticipantsReady = 5,

    /// <summary>
    /// The media reached its end; playback stopped on the last position.
    /// </summary>
    Ended = 6,

    /// <summary>
    /// The host started playback without waiting any longer for participants that were not ready.
    /// </summary>
    StartedWithoutWaiting = 7,
}
