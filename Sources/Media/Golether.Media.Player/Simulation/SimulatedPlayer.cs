using Golether.Core.Playback;
using Golether.Core.Time;

namespace Golether.Media.Player.Simulation;

/// <summary>
/// A player without video: the position advances with the monotonic clock.
/// </summary>
/// <remarks>
/// Used when libmpv is not installed and in tests, so synchronization between application instances can be checked
/// without real media output. The class is thread-safe.
/// </remarks>
public sealed class SimulatedPlayer : IPlaybackController
{
    /// <summary>
    /// The clock.
    /// </summary>
    private readonly IMonotonicClock _clock;

    /// <summary>
    /// Guards the state.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Whether a source is loaded.
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// Whether the player is paused.
    /// </summary>
    private bool _paused = true;

    /// <summary>
    /// The position at <see cref="_anchorTime"/>.
    /// </summary>
    private TimeSpan _anchorPosition;

    /// <summary>
    /// The clock time of the anchor, microseconds.
    /// </summary>
    private long _anchorTime;

    /// <summary>
    /// The rate.
    /// </summary>
    private double _rate = 1.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulatedPlayer"/> class.
    /// </summary>
    /// <param name="clock">The clock.</param>
    /// <param name="duration">The simulated media duration.</param>
    public SimulatedPlayer(IMonotonicClock clock, TimeSpan duration)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Duration = duration;
    }

    /// <summary>
    /// Gets the simulated media duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the loaded source.
    /// </summary>
    public Uri? Source { get; private set; }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public PlayerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _loaded
                ? new PlayerSnapshot(true, CurrentPosition(), Duration, _paused, false, TimeSpan.FromSeconds(30), _rate)
                : PlayerSnapshot.Empty;
        }
    }

    /// <inheritdoc />
    public Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _loaded = true;
            _paused = paused;
            Anchor(startPosition);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Anchor(CurrentPosition());
            _paused = paused;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Anchor(position);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetRateAsync(double rate, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Anchor(CurrentPosition());
            _rate = rate;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _loaded = false;
            _paused = true;
            Source = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Computes the current position; the caller holds the lock.
    /// </summary>
    /// <returns>The position clamped to the duration.</returns>
    private TimeSpan CurrentPosition()
    {
        if (_paused)
        {
            return _anchorPosition;
        }

        var elapsed = Microseconds.ToTimeSpan((long)((_clock.NowMicroseconds - _anchorTime) * _rate));
        var position = _anchorPosition + elapsed;
        return position > Duration ? Duration : position;
    }

    /// <summary>
    /// Moves the anchor; the caller holds the lock.
    /// </summary>
    /// <param name="position">The new anchor position.</param>
    private void Anchor(TimeSpan position)
    {
        _anchorPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
        _anchorTime = _clock.NowMicroseconds;
    }
}
