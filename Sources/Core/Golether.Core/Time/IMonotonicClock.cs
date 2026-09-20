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
