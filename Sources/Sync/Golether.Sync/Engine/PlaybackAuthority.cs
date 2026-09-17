using Golether.Core.Identity;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Core.Time;
using Golether.Sync.Protocol;

namespace Golether.Sync.Engine;

/// <summary>
/// How the host treats participants that cannot keep up.
/// </summary>
public enum BufferingPolicy
{
    /// <summary>
    /// Pause everybody until lagging participants have buffered enough data.
    /// </summary>
    WaitForAll = 0,

    /// <summary>
    /// Keep playing; lagging participants catch up on their own.
    /// </summary>
    LetLaggardsCatchUp = 1,
}

/// <summary>
/// Settings of <see cref="PlaybackAuthority"/>.
/// </summary>
public sealed record PlaybackAuthorityOptions
{
    /// <summary>
    /// Gets the minimal lead of a scheduled start on top of the largest round trip (default 1.5 s).
    /// </summary>
    public TimeSpan StartLead { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Gets the upper bound of the start delay (default 8 s).
    /// </summary>
    public TimeSpan MaxStartDelay { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Gets the buffering policy (default <see cref="BufferingPolicy.WaitForAll"/>).
    /// </summary>
    public BufferingPolicy BufferingPolicy { get; init; } = BufferingPolicy.WaitForAll;

    /// <summary>
    /// Gets how far a buffering participant may fall behind before everybody waits (default 2 s).
    /// </summary>
    public TimeSpan WaitThreshold { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the amount of data every participant needs before playback resumes (default 5 s).
    /// </summary>
    public TimeSpan ResumeCacheAhead { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The authoritative playback state of the host.
/// </summary>
/// <remarks>
/// Every participant may request play, pause or seek; the host applies requests in arrival order (the last one wins),
/// assigns increasing versions and broadcasts the result. The class is thread-safe.
/// </remarks>
public sealed class PlaybackAuthority
{
    /// <summary>
    /// The host identifier used for automatic changes.
    /// </summary>
    private readonly PeerId _host;

    /// <summary>
    /// The session clock.
    /// </summary>
    private readonly ISessionClock _clock;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly PlaybackAuthorityOptions _options;

    /// <summary>
    /// Guards the state.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The current state.
    /// </summary>
    private PlaybackState _current;

    /// <summary>
    /// Whether the current pause was made automatically while waiting for participants.
    /// </summary>
    private bool _holding;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackAuthority"/> class.
    /// </summary>
    /// <param name="host">The host identifier.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="options">The options.</param>
    public PlaybackAuthority(PeerId host, ISessionClock clock, PlaybackAuthorityOptions? options = null)
    {
        _host = host;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new PlaybackAuthorityOptions();
        BufferingPolicy = _options.BufferingPolicy;
        _current = PlaybackState.Initial(host, clock.NowMicroseconds);
    }

    /// <summary>
    /// Gets the media duration used to clamp positions; <see langword="null"/> while unknown.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Gets or sets the buffering policy; initialized from <see cref="PlaybackAuthorityOptions.BufferingPolicy"/>.
    /// </summary>
    public BufferingPolicy BufferingPolicy { get; set; }

    /// <summary>
    /// Gets the current state.
    /// </summary>
    public PlaybackState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Resets the state for a new media file.
    /// </summary>
    /// <returns>The new state.</returns>
    public PlaybackState Reset()
    {
        lock (_gate)
        {
            _holding = false;
            _current = PlaybackState.Initial(_host, _clock.NowMicroseconds) with { Version = _current.Version + 1 };
            return _current;
        }
    }

    /// <summary>
    /// Applies a request of a participant.
    /// </summary>
    /// <param name="origin">The authenticated participant.</param>
    /// <param name="request">The request.</param>
    /// <param name="maxRoundTrip">The largest round trip among the participants, used to schedule starts.</param>
    /// <returns>The new state to broadcast, or <see langword="null"/> when nothing changed.</returns>
    /// <exception cref="ArgumentException">A seek request has no position.</exception>
    public PlaybackState? Apply(PeerId origin, PlaybackRequest request, TimeSpan maxRoundTrip)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind == PlaybackRequestKind.Seek && request.Position is null)
        {
            throw new ArgumentException("A seek request must specify a position.", nameof(request));
        }

        lock (_gate)
        {
            var now = _clock.NowMicroseconds;
            var expected = _current.ExpectedPositionAt(now);
            var startAt = now + Microseconds.From(StartDelay(maxRoundTrip));
            PlaybackState next;
            switch (request.Kind)
            {
                case PlaybackRequestKind.Play:
                    if (_current.State == PlayState.Playing && request.Position is null)
                    {
                        return null;
                    }

                    next = _current with
                    {
                        State = PlayState.Playing,
                        Position = Clamp(request.Position ?? expected),
                        ReferenceTime = startAt,
                        Cause = PlaybackCause.Play,
                    };
                    break;

                case PlaybackRequestKind.Pause:
                    if (_current.State == PlayState.Paused && request.Position is null)
                    {
                        return null;
                    }

                    next = _current with
                    {
                        State = PlayState.Paused,
                        Position = Clamp(request.Position ?? expected),
                        ReferenceTime = now,
                        Cause = PlaybackCause.Pause,
                    };
                    break;

                default:
                    next = _current with
                    {
                        Position = Clamp(request.Position!.Value),
                        ReferenceTime = _current.State == PlayState.Playing ? startAt : now,
                        Cause = PlaybackCause.Seek,
                    };
                    break;
            }

            _holding = false;
            return Commit(next, origin);
        }
    }

    /// <summary>
    /// Applies the buffering policy to the latest participant statuses.
    /// </summary>
    /// <param name="statuses">The statuses of all participants except the host.</param>
    /// <param name="maxRoundTrip">The largest round trip among the participants.</param>
    /// <returns>The new state to broadcast, or <see langword="null"/> when nothing changed.</returns>
    public PlaybackState? EvaluateBuffering(IReadOnlyCollection<ParticipantStatus> statuses, TimeSpan maxRoundTrip)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        lock (_gate)
        {
            var now = _clock.NowMicroseconds;
            if (_holding)
            {
                if (_current.State != PlayState.Paused)
                {
                    _holding = false;
                    return null;
                }

                if (statuses.Any(s => s.IsBuffering || s.CacheAhead < _options.ResumeCacheAhead))
                {
                    return null;
                }

                _holding = false;
                return Commit(
                    _current with
                    {
                        State = PlayState.Playing,
                        ReferenceTime = now + Microseconds.From(StartDelay(maxRoundTrip)),
                        Cause = PlaybackCause.ParticipantsReady,
                    },
                    _host);
            }

            if (BufferingPolicy != BufferingPolicy.WaitForAll
                || _current.State != PlayState.Playing
                || _current.IsScheduledAfter(now)
                || !statuses.Any(s => s.IsBuffering && -s.Drift >= _options.WaitThreshold))
            {
                return null;
            }

            _holding = true;
            return Commit(
                _current with
                {
                    State = PlayState.Paused,
                    Position = Clamp(_current.ExpectedPositionAt(now)),
                    ReferenceTime = now,
                    Cause = PlaybackCause.WaitingForParticipants,
                },
                _host);
        }
    }

    /// <summary>
    /// Computes the delay of a scheduled start.
    /// </summary>
    /// <param name="maxRoundTrip">The largest round trip.</param>
    /// <returns>The delay.</returns>
    private TimeSpan StartDelay(TimeSpan maxRoundTrip)
    {
        var delay = (maxRoundTrip > TimeSpan.Zero ? maxRoundTrip : TimeSpan.Zero) + _options.StartLead;
        return delay > _options.MaxStartDelay ? _options.MaxStartDelay : delay;
    }

    /// <summary>
    /// Clamps a position to the media.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The clamped position.</returns>
    private TimeSpan Clamp(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return Duration is { } duration && position > duration ? duration : position;
    }

    /// <summary>
    /// Stores a new state with the next version; the caller holds the lock.
    /// </summary>
    /// <param name="next">The state.</param>
    /// <param name="origin">The originator.</param>
    /// <returns>The committed state.</returns>
    private PlaybackState Commit(PlaybackState next, PeerId origin)
    {
        _current = next with { Version = _current.Version + 1, Origin = origin };
        return _current;
    }
}
