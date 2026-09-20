namespace Golether.Core.Time;

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
