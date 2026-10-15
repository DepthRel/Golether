using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Time;

namespace Golether.Core.Playback;

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
    /// The largest position a state may carry. No film is that long; without a ceiling a position taken from the
    /// network could sit at the limit of <see cref="TimeSpan"/> and overflow in the first subtraction.
    /// </summary>
    public static readonly TimeSpan MaxPosition = TimeSpan.FromDays(30);

    /// <summary>
    /// The slowest nominal rate accepted from the network. Together with the drift correction it stays inside what
    /// a player accepts.
    /// </summary>
    public const double MIN_RATE = 0.5;

    /// <summary>
    /// The fastest nominal rate accepted from the network.
    /// </summary>
    public const double MAX_RATE = 2.0;

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
    /// Checks a state received from the network: a known mode, a position inside <see cref="MaxPosition"/> and a
    /// finite rate between <see cref="MIN_RATE"/> and <see cref="MAX_RATE"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the state can be followed.</returns>
    public bool IsValid()
        => Enum.IsDefined(State) &&
           Position >= TimeSpan.Zero && Position <= MaxPosition &&
           double.IsFinite(Rate) && Rate is >= MIN_RATE and <= MAX_RATE;

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

        // The position comes from the network (a participant may ask for any): near the largest value the sum
        // stays at the limit instead of overflowing.
        var ticks = (Int128)Position.Ticks + Microseconds.ToTimeSpan(elapsed).Ticks;
        return TimeSpan.FromTicks((long)Int128.Clamp(ticks, TimeSpan.MinValue.Ticks, TimeSpan.MaxValue.Ticks));
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
