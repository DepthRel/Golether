namespace Golether.Core.Time;

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
