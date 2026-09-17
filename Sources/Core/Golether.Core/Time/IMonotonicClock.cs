using System.Diagnostics;

namespace Golether.Core.Time;

/// <summary>
/// A monotonic clock with microsecond resolution used for playback synchronization.
/// </summary>
/// <remarks>
/// Wall-clock time can jump (NTP corrections, manual changes), so synchronization uses a monotonic source.
/// Values from different processes are not comparable; the session clock maps them to the host timeline.
/// </remarks>
public interface IMonotonicClock
{
    /// <summary>
    /// Gets the current value in microseconds from an arbitrary process-local origin.
    /// </summary>
    long NowMicroseconds { get; }
}

/// <summary>
/// <see cref="IMonotonicClock"/> based on <see cref="Stopwatch"/>.
/// </summary>
public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    /// <summary>
    /// The shared instance.
    /// </summary>
    public static StopwatchMonotonicClock Instance { get; } = new();

    /// <inheritdoc />
    public long NowMicroseconds => (long)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));
}

/// <summary>
/// Conversions between <see cref="TimeSpan"/> and session microseconds.
/// </summary>
public static class Microseconds
{
    /// <summary>
    /// Converts a time span to microseconds.
    /// </summary>
    /// <param name="value">The time span.</param>
    /// <returns>The number of microseconds.</returns>
    public static long From(TimeSpan value) => value.Ticks / TimeSpan.TicksPerMicrosecond;

    /// <summary>
    /// Converts microseconds to a time span.
    /// </summary>
    /// <param name="microseconds">The number of microseconds.</param>
    /// <returns>The time span.</returns>
    public static TimeSpan ToTimeSpan(long microseconds) => TimeSpan.FromTicks(microseconds * TimeSpan.TicksPerMicrosecond);
}
