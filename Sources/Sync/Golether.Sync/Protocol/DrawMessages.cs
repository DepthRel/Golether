using Golether.Core.Identity;

namespace Golether.Sync.Protocol;

/// <summary>
/// The part of a stroke a message carries.
/// </summary>
public enum StrokePhase
{
    /// <summary>
    /// The pen touched the picture: a new stroke starts.
    /// </summary>
    Start = 0,

    /// <summary>
    /// The pen moves: more points of the same stroke.
    /// </summary>
    Continue = 1,

    /// <summary>
    /// The pen was lifted: the stroke is finished and starts to fade.
    /// </summary>
    End = 2,
}

/// <summary>
/// A point of a stroke as a share of the video picture, so every participant draws it in the right place whatever
/// the size of their window.
/// </summary>
/// <param name="X">The horizontal share, 0–1.</param>
/// <param name="Y">The vertical share, 0–1.</param>
public readonly record struct StrokePoint(float X, float Y);

/// <summary>
/// A piece of a stroke drawn over the video. Participants send it without <paramref name="Sender"/>; the host sets
/// the authenticated sender and relays it to everybody.
/// </summary>
/// <param name="StrokeId">The identifier of the stroke, chosen by the sender.</param>
/// <param name="Phase">Which part of the stroke this is.</param>
/// <param name="Points">The new points, in order.</param>
/// <param name="Sender">The sender, set by the host only.</param>
public sealed record DrawMessage(
    string StrokeId,
    StrokePhase Phase,
    IReadOnlyList<StrokePoint> Points,
    PeerId? Sender) : SessionMessage
{
    /// <summary>
    /// The largest number of points in one message.
    /// </summary>
    public const int MaxPoints = 128;

    /// <summary>
    /// The largest length of a stroke identifier.
    /// </summary>
    public const int MaxIdLength = 32;

    /// <summary>
    /// Checks a received message and brings its points into the picture.
    /// </summary>
    /// <returns>The clean message, or <see langword="null"/> when it must be dropped.</returns>
    public DrawMessage? Sanitize()
    {
        if (string.IsNullOrEmpty(StrokeId) || StrokeId.Length > MaxIdLength || !StrokeId.All(char.IsAsciiHexDigit)
            || !Enum.IsDefined(Phase) || Points is null || Points.Count > MaxPoints)
        {
            return null;
        }

        if (Points.Count == 0)
        {
            return Phase == StrokePhase.End ? this with { Points = [] } : null;
        }

        var points = new List<StrokePoint>(Points.Count);
        foreach (var point in Points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                continue;
            }

            points.Add(new StrokePoint(Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1)));
        }

        return points.Count == 0 && Phase != StrokePhase.End ? null : this with { Points = points };
    }

    /// <summary>
    /// Creates a message of this device.
    /// </summary>
    /// <param name="strokeId">The stroke.</param>
    /// <param name="phase">The part of the stroke.</param>
    /// <param name="points">The points.</param>
    /// <returns>The message, or <see langword="null"/> when there is nothing to send.</returns>
    public static DrawMessage? Create(string strokeId, StrokePhase phase, IReadOnlyList<StrokePoint> points)
        => new DrawMessage(strokeId, phase, points ?? [], null).Sanitize();

    /// <summary>
    /// Creates an identifier for a new stroke.
    /// </summary>
    /// <returns>The identifier.</returns>
    public static string CreateStrokeId() => Convert.ToHexString(Guid.NewGuid().ToByteArray())[..24];
}

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
