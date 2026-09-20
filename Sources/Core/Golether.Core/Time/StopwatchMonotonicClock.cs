using System.Diagnostics;

namespace Golether.Core.Time;

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
