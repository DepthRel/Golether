using Golether.Core.Time;

namespace Golether.Sync.Tests;

/// <summary>
/// A clock the test moves by hand; used both as monotonic and as session clock.
/// </summary>
internal sealed class ManualClock : IMonotonicClock, ISessionClock
{
    /// <summary>
    /// Gets or sets the current time in microseconds.
    /// </summary>
    public long NowMicroseconds { get; set; }

    /// <inheritdoc />
    public bool IsSynchronized => true;

    /// <summary>
    /// Moves the clock forward.
    /// </summary>
    /// <param name="delta">The time to add.</param>
    public void Advance(TimeSpan delta) => NowMicroseconds += Microseconds.From(delta);
}
