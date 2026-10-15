namespace Golether.Core.Time;

/// <summary>
/// <see cref="IMonotonicClock"/> on the monotonic timestamps of <see cref="TimeProvider.System"/>.
/// </summary>
public sealed class SystemMonotonicClock : IMonotonicClock
{
    /// <summary>
    /// The shared instance.
    /// </summary>
    public static SystemMonotonicClock Instance { get; } = new();

    /// <inheritdoc />
    public long NowMicroseconds =>
        (long)(TimeProvider.System.GetTimestamp() * (1_000_000.0 / TimeProvider.System.TimestampFrequency));
}
