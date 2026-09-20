using Golether.Core.Time;

namespace Golether.Sync.Clock;

/// <summary>
/// Session clock of a participant: local monotonic time plus the estimated offset to the host.
/// </summary>
public sealed class ParticipantSessionClock : ISessionClock
{
    /// <summary>
    /// The local clock.
    /// </summary>
    private readonly IMonotonicClock _clock;

    /// <summary>
    /// The offset estimator.
    /// </summary>
    private readonly ClockOffsetEstimator _estimator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantSessionClock"/> class.
    /// </summary>
    /// <param name="clock">The local clock.</param>
    /// <param name="estimator">The offset estimator.</param>
    public ParticipantSessionClock(IMonotonicClock clock, ClockOffsetEstimator estimator)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
    }

    /// <inheritdoc />
    public long NowMicroseconds => _clock.NowMicroseconds + _estimator.OffsetMicroseconds;

    /// <inheritdoc />
    public bool IsSynchronized => _estimator.HasEstimate;
}
