namespace Golether.Sync.Protocol;

/// <summary>
/// Limits how often something may happen: a sliding window with a counter.
/// </summary>
public sealed class SlidingRateLimiter
{
    /// <summary>
    /// The window.
    /// </summary>
    private readonly TimeSpan _window;

    /// <summary>
    /// The allowed number of events per window.
    /// </summary>
    private readonly int _limit;

    /// <summary>
    /// The times of the recent events.
    /// </summary>
    private readonly Queue<long> _recent = new();

    /// <summary>
    /// Guards the queue.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingRateLimiter"/> class.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="limit">The allowed number of events per window.</param>
    public SlidingRateLimiter(TimeSpan window, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        _window = window;
        _limit = limit;
    }

    /// <summary>
    /// Checks whether an event may pass now and counts it.
    /// </summary>
    /// <param name="timestamp">The current time in <see cref="TimeProvider.GetTimestamp"/> units.</param>
    /// <param name="timeProvider">The time provider of <paramref name="timestamp"/>.</param>
    /// <returns><see langword="true"/> when the event may pass.</returns>
    public bool TryAcquire(long timestamp, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        lock (_gate)
        {
            while (_recent.Count > 0 && timeProvider.GetElapsedTime(_recent.Peek(), timestamp) >= _window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= _limit)
            {
                return false;
            }

            _recent.Enqueue(timestamp);
            return true;
        }
    }
}
