using Golether.Core.Playback;
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

/// <summary>
/// A player that records commands and advances its position with a <see cref="ManualClock"/>.
/// </summary>
internal sealed class FakePlayer : IPlaybackController
{
    /// <summary>
    /// The clock.
    /// </summary>
    private readonly ManualClock _clock;

    /// <summary>
    /// The position at <see cref="_anchor"/>.
    /// </summary>
    private TimeSpan _position;

    /// <summary>
    /// The clock time of the anchor.
    /// </summary>
    private long _anchor;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakePlayer"/> class.
    /// </summary>
    /// <param name="clock">The clock.</param>
    public FakePlayer(ManualClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Gets the recorded commands.
    /// </summary>
    public List<string> Commands { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether media is loaded.
    /// </summary>
    public bool Loaded { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the player is paused.
    /// </summary>
    public bool Paused { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the player is buffering.
    /// </summary>
    public bool Buffering { get; set; }

    /// <summary>
    /// Gets or sets the rate.
    /// </summary>
    public double Rate { get; set; } = 1.0;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>
    /// Gets the current position.
    /// </summary>
    public TimeSpan Position
    {
        get
        {
            var position = Paused ? _position : _position + Microseconds.ToTimeSpan((long)((_clock.NowMicroseconds - _anchor) * Rate));
            return position > Duration ? Duration : position;
        }
    }

    /// <summary>
    /// Gets or sets the media duration.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Gets a value indicating whether playback stopped at the end (mpv with <c>keep-open</c> pauses there).
    /// </summary>
    public bool AtEnd => !Paused && Position >= Duration;

    /// <summary>
    /// Moves the position without recording a command (simulates drift).
    /// </summary>
    /// <param name="position">The new position.</param>
    public void Jump(TimeSpan position)
    {
        _position = position;
        _anchor = _clock.NowMicroseconds;
    }

    /// <inheritdoc />
    public PlayerSnapshot GetSnapshot()
        => Loaded ? new PlayerSnapshot(true, Position, Duration, Paused || AtEnd, Buffering, TimeSpan.FromSeconds(10), Rate) : PlayerSnapshot.Empty;

    /// <inheritdoc />
    public Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default)
    {
        Commands.Add($"load {startPosition.TotalSeconds}");
        Loaded = true;
        Paused = paused;
        Jump(startPosition);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        Jump(Position);
        if (Paused != paused)
        {
            Commands.Add(paused ? "pause" : "play");
        }

        Paused = paused;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        Commands.Add($"seek {position.TotalSeconds:0.###}");
        Jump(position);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetRateAsync(double rate, CancellationToken cancellationToken = default)
    {
        Jump(Position);
        if (Math.Abs(Rate - rate) > 1e-9)
        {
            Commands.Add($"rate {rate:0.###}");
        }

        Rate = rate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Commands.Add("stop");
        Loaded = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
