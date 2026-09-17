namespace Golether.Core.Time;

/// <summary>
/// The shared session timeline: the monotonic clock of the host, as seen from the local process.
/// </summary>
/// <remarks>
/// On the host the session clock equals its monotonic clock. On a participant it is the local monotonic clock plus the
/// estimated offset to the host clock.
/// </remarks>
public interface ISessionClock
{
    /// <summary>
    /// Gets the current session time in microseconds.
    /// </summary>
    long NowMicroseconds { get; }

    /// <summary>
    /// Gets a value indicating whether the clock is synchronized with the host (always <see langword="true"/> on the
    /// host).
    /// </summary>
    bool IsSynchronized { get; }
}

/// <summary>
/// Session clock of the host: the local monotonic clock itself.
/// </summary>
public sealed class HostSessionClock : ISessionClock
{
    /// <summary>
    /// The local monotonic clock.
    /// </summary>
    private readonly IMonotonicClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostSessionClock"/> class.
    /// </summary>
    /// <param name="clock">The local monotonic clock.</param>
    public HostSessionClock(IMonotonicClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public long NowMicroseconds => _clock.NowMicroseconds;

    /// <inheritdoc />
    public bool IsSynchronized => true;
}
