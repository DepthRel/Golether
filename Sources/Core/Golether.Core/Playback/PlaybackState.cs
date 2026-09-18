using Golether.Core.Identity;
using Golether.Core.Time;

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

/// <summary>
/// The authoritative playback state of a session, expressed in time rather than as commands.
/// </summary>
/// <remarks>
/// "At session time <see cref="ReferenceTime"/> the position was <see cref="Position"/> and playback advanced with
/// <see cref="Rate"/>". Each node computes the expected position itself, so the delivery delay of the state does not
/// affect the result. A <see cref="PlayState.Playing"/> state with a reference time in the future is a scheduled start.
/// </remarks>
public sealed record PlaybackState
{
    /// <summary>
    /// Gets the playback mode.
    /// </summary>
    public required PlayState State { get; init; }

    /// <summary>
    /// Gets the media position at <see cref="ReferenceTime"/>.
    /// </summary>
    public required TimeSpan Position { get; init; }

    /// <summary>
    /// Gets the session time (host monotonic clock, microseconds) the position refers to.
    /// </summary>
    public required long ReferenceTime { get; init; }

    /// <summary>
    /// Gets the nominal playback rate.
    /// </summary>
    public double Rate { get; init; } = 1.0;

    /// <summary>
    /// Gets the version assigned by the host; a node applies only versions greater than the one it has applied.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Gets the participant that caused the change.
    /// </summary>
    public required PeerId Origin { get; init; }

    /// <summary>
    /// Gets the reason of the change.
    /// </summary>
    public PlaybackCause Cause { get; init; }

    /// <summary>
    /// Creates the initial paused state of a session.
    /// </summary>
    /// <param name="host">The host identifier.</param>
    /// <param name="now">The current session time.</param>
    /// <returns>The state with version 0 at position zero.</returns>
    public static PlaybackState Initial(PeerId host, long now) => new()
    {
        State = PlayState.Paused,
        Position = TimeSpan.Zero,
        ReferenceTime = now,
        Version = 0,
        Origin = host,
        Cause = PlaybackCause.Initial,
    };

    /// <summary>
    /// Computes the position every node should show at the given session time.
    /// </summary>
    /// <param name="sessionTime">The session time in microseconds.</param>
    /// <returns>The expected position; never before <see cref="Position"/> and never negative.</returns>
    public TimeSpan ExpectedPositionAt(long sessionTime)
    {
        if (State != PlayState.Playing || sessionTime <= ReferenceTime)
        {
            return Position;
        }

        var elapsed = (long)((sessionTime - ReferenceTime) * Rate);
        return Position + Microseconds.ToTimeSpan(elapsed);
    }

    /// <summary>
    /// Returns the expected position, never beyond the end of the media.
    /// </summary>
    /// <param name="sessionTime">The session time in microseconds.</param>
    /// <param name="duration">The media duration, or <see langword="null"/> while unknown.</param>
    /// <returns>The position.</returns>
    public TimeSpan ExpectedPositionAt(long sessionTime, TimeSpan? duration)
    {
        var position = ExpectedPositionAt(sessionTime);
        return duration is { } end && end > TimeSpan.Zero && position > end ? end : position;
    }

    /// <summary>
    /// Determines whether the state is a start scheduled after the given session time.
    /// </summary>
    /// <param name="sessionTime">The session time in microseconds.</param>
    /// <returns><see langword="true"/> when playback must wait until <see cref="ReferenceTime"/>.</returns>
    public bool IsScheduledAfter(long sessionTime) => State == PlayState.Playing && sessionTime < ReferenceTime;
}
