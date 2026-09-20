using Golether.Core.Data.Enums;

namespace Golether.Sync.Protocol;

/// <summary>
/// Limits how often a participant may send chat messages: a sliding window per kind.
/// </summary>
public sealed class ChatRateLimiter
{
    /// <summary>
    /// The window.
    /// </summary>
    private readonly TimeSpan _window;

    /// <summary>
    /// The allowed number of texts per window.
    /// </summary>
    private readonly int _texts;

    /// <summary>
    /// The allowed number of reactions per window.
    /// </summary>
    private readonly int _reactions;

    /// <summary>
    /// The recent send times of texts.
    /// </summary>
    private readonly Queue<long> _recentTexts = new();

    /// <summary>
    /// The recent send times of reactions.
    /// </summary>
    private readonly Queue<long> _recentReactions = new();

    /// <summary>
    /// Guards the queues.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatRateLimiter"/> class.
    /// </summary>
    /// <param name="window">The window (5 s by default).</param>
    /// <param name="texts">The allowed texts per window (5 by default).</param>
    /// <param name="reactions">The allowed reactions per window (15 by default).</param>
    public ChatRateLimiter(TimeSpan? window = null, int texts = 5, int reactions = 15)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
        _texts = texts;
        _reactions = reactions;
    }

    /// <summary>
    /// Checks whether a message may pass now and counts it.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="timestamp">The current time in <see cref="TimeProvider.GetTimestamp"/> units.</param>
    /// <param name="timeProvider">The time provider of <paramref name="timestamp"/>.</param>
    /// <returns><see langword="true"/> when the message may pass.</returns>
    public bool TryAcquire(ChatKind kind, long timestamp, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var (queue, limit) = kind == ChatKind.Reaction ? (_recentReactions, _reactions) : (_recentTexts, _texts);
        lock (_gate)
        {
            while (queue.Count > 0 && timeProvider.GetElapsedTime(queue.Peek(), timestamp) >= _window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(timestamp);
            return true;
        }
    }
}
