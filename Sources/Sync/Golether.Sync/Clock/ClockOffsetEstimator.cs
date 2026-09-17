using Golether.Core.Time;

namespace Golether.Sync.Clock;

/// <summary>
/// Estimates the offset between the local monotonic clock and the host clock from NTP-style probes.
/// </summary>
/// <remarks>
/// For a probe sent at local time t0, received by the host at t1, answered at t2 and received back at t3:
/// round trip = (t3 − t0) − (t2 − t1), offset = ((t1 − t0) + (t2 − t3)) / 2. The error of the offset is bounded by half
/// the round trip of the sample, so the estimator uses the sample with the smallest round trip in a sliding window.
/// This keeps the error at a few tens of milliseconds even when the round trip exceeds one second, as long as the path
/// is roughly symmetric. The class is thread-safe.
/// </remarks>
public sealed class ClockOffsetEstimator
{
    /// <summary>
    /// The sliding window of samples.
    /// </summary>
    private readonly Queue<(long Offset, long RoundTrip)> _samples = new();

    /// <summary>
    /// The window size.
    /// </summary>
    private readonly int _windowSize;

    /// <summary>
    /// Guards the samples.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The current best offset.
    /// </summary>
    private long _offset;

    /// <summary>
    /// The round trip of the best sample.
    /// </summary>
    private long _bestRoundTrip;

    /// <summary>
    /// The median round trip of the window.
    /// </summary>
    private long _medianRoundTrip;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClockOffsetEstimator"/> class.
    /// </summary>
    /// <param name="windowSize">The number of recent samples considered (default 16).</param>
    public ClockOffsetEstimator(int windowSize = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1);
        _windowSize = windowSize;
    }

    /// <summary>
    /// Gets a value indicating whether at least one valid sample was added.
    /// </summary>
    public bool HasEstimate
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count > 0;
            }
        }
    }

    /// <summary>
    /// Gets the offset to add to the local clock to obtain the host clock, in microseconds.
    /// </summary>
    public long OffsetMicroseconds
    {
        get
        {
            lock (_gate)
            {
                return _offset;
            }
        }
    }

    /// <summary>
    /// Gets the median round trip of recent samples, for display; <see langword="null"/> without samples.
    /// </summary>
    public TimeSpan? RoundTrip
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count == 0 ? null : Microseconds.ToTimeSpan(_medianRoundTrip);
            }
        }
    }

    /// <summary>
    /// Gets the uncertainty of the offset (half the best round trip); <see langword="null"/> without samples.
    /// </summary>
    public TimeSpan? Uncertainty
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count == 0 ? null : Microseconds.ToTimeSpan(_bestRoundTrip / 2);
            }
        }
    }

    /// <summary>
    /// Adds a probe result.
    /// </summary>
    /// <param name="clientSend">t0, local time when the probe was sent.</param>
    /// <param name="hostReceive">t1, host time when the probe arrived.</param>
    /// <param name="hostSend">t2, host time when the answer was sent.</param>
    /// <param name="clientReceive">t3, local time when the answer arrived.</param>
    /// <returns><see langword="false"/> when the sample is inconsistent and was ignored.</returns>
    public bool AddSample(long clientSend, long hostReceive, long hostSend, long clientReceive)
    {
        if (clientReceive < clientSend || hostSend < hostReceive)
        {
            return false;
        }

        var roundTrip = (clientReceive - clientSend) - (hostSend - hostReceive);
        if (roundTrip < 0)
        {
            return false;
        }

        var offset = ((hostReceive - clientSend) + (hostSend - clientReceive)) / 2;
        lock (_gate)
        {
            _samples.Enqueue((offset, roundTrip));
            while (_samples.Count > _windowSize)
            {
                _samples.Dequeue();
            }

            var best = _samples.MinBy(s => s.RoundTrip);
            _offset = best.Offset;
            _bestRoundTrip = best.RoundTrip;
            var sorted = _samples.Select(s => s.RoundTrip).Order().ToArray();
            _medianRoundTrip = sorted[sorted.Length / 2];
        }

        return true;
    }
}

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
