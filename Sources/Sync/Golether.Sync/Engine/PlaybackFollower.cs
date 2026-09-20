using Golether.Core.Data.Enums;
using Golether.Core.Playback;
using Golether.Core.Time;
using Golether.Sync.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Golether.Sync.Engine;

/// <summary>
/// Keeps the local player on the authoritative session state: applies new versions, starts scheduled playback on
/// time and corrects drift. Every node runs a follower, the host included.
/// </summary>
/// <remarks>
/// Methods must not be called concurrently; the session drives the follower from one loop.
/// </remarks>
public sealed class PlaybackFollower
{
    /// <summary>
    /// The drift below which a paused player is not moved.
    /// </summary>
    private static readonly TimeSpan PausedTolerance = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// The time after a seek during which the player position is not trusted.
    /// </summary>
    private static readonly TimeSpan SeekSettleTime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How close to the duration a position counts as the end.
    /// </summary>
    private static readonly TimeSpan EndTolerance = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The player.
    /// </summary>
    private readonly IPlaybackController _player;

    /// <summary>
    /// The session clock.
    /// </summary>
    private readonly ISessionClock _clock;

    /// <summary>
    /// The drift corrector.
    /// </summary>
    private readonly DriftCorrector _corrector;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The session time of the last seek.
    /// </summary>
    private long _lastSeekAt = long.MinValue;

    /// <summary>
    /// Whether the applied state has been pushed to the loaded player.
    /// </summary>
    private bool _appliedToPlayer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackFollower"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="clock">The session clock.</param>
    /// <param name="corrector">The drift corrector.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackFollower(IPlaybackController player, ISessionClock clock, DriftCorrector corrector, ILogger? logger = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _corrector = corrector ?? throw new ArgumentNullException(nameof(corrector));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the last applied authoritative state.
    /// </summary>
    public PlaybackState? Applied { get; private set; }

    /// <summary>
    /// Accepts an authoritative state.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false"/> when the state is not newer than the applied one.</returns>
    public async Task<bool> ApplyAsync(PlaybackState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Applied is not null && state.Version <= Applied.Version)
        {
            return false;
        }

        Applied = state;
        _appliedToPlayer = false;
        await PushToPlayerAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Forgets the applied state, for example when a new media file is loaded.
    /// </summary>
    public void Reset()
    {
        Applied = null;
        _appliedToPlayer = false;
    }

    /// <summary>
    /// Applies a local intent immediately, before the host confirms it, so the user sees an instant reaction.
    /// </summary>
    /// <param name="request">The intent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player accepted the commands.</returns>
    public async Task ApplyLocalIntentAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_player.GetSnapshot().IsLoaded)
        {
            return;
        }

        switch (request.Kind)
        {
            case PlaybackRequestKind.Pause:
                await _player.SetPausedAsync(true, cancellationToken).ConfigureAwait(false);
                break;
            case PlaybackRequestKind.Seek when request.Position is { } position:
                await SeekAsync(position, cancellationToken).ConfigureAwait(false);
                break;
        }

        // A local play waits for the scheduled start from the host, so everybody starts together.
    }

    /// <summary>
    /// Runs periodic work: pushes a pending state to a newly loaded player, starts scheduled playback and corrects drift.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status.</returns>
    public async Task<FollowerStatus> TickAsync(CancellationToken cancellationToken)
    {
        var snapshot = _player.GetSnapshot();
        var state = Applied;
        if (state is null || !snapshot.IsLoaded)
        {
            return new FollowerStatus(snapshot, state?.Position, TimeSpan.Zero, null);
        }

        if (!_appliedToPlayer)
        {
            await PushToPlayerAsync(cancellationToken).ConfigureAwait(false);
            snapshot = _player.GetSnapshot();
        }

        var now = _clock.NowMicroseconds;
        var expected = state.ExpectedPositionAt(now, snapshot.Duration);
        var drift = snapshot.Position is { } position ? position - expected : TimeSpan.Zero;
        if (IsAtEnd(expected, snapshot) && snapshot.Position is { } last && IsAtEnd(last, snapshot))
        {
            // Both the session and the player are at the end: nothing to start, seek or correct.
            return new FollowerStatus(snapshot, expected, TimeSpan.Zero, null);
        }

        if (state.IsScheduledAfter(now))
        {
            return new FollowerStatus(snapshot, expected, drift, Microseconds.ToTimeSpan(state.ReferenceTime - now));
        }

        if (state.State == PlayState.Playing && snapshot.IsPaused)
        {
            // The scheduled start has come.
            if (drift.Duration() > _corrector.Options.DeadBand)
            {
                await SeekAsync(expected, cancellationToken).ConfigureAwait(false);
            }

            await _player.SetPausedAsync(false, cancellationToken).ConfigureAwait(false);
            return new FollowerStatus(snapshot, expected, drift, null);
        }

        if (state.State != PlayState.Playing || snapshot.IsBuffering || snapshot.Position is null
            || now - _lastSeekAt < Microseconds.From(SeekSettleTime))
        {
            return new FollowerStatus(snapshot, expected, drift, null);
        }

        var decision = _corrector.Decide(expected, snapshot.Position.Value, state.Rate, snapshot.Rate);
        switch (decision.Action)
        {
            case DriftAction.AdjustRate:
                _logger.LogDebug("Drift {Drift} ms: rate {Rate:F3}", (int)decision.Drift.TotalMilliseconds, decision.Rate);
                await _player.SetRateAsync(decision.Rate, cancellationToken).ConfigureAwait(false);
                break;
            case DriftAction.Seek:
                _logger.LogInformation("Drift {Drift} ms: seeking to {Position}", (int)decision.Drift.TotalMilliseconds, decision.Position);
                await _player.SetRateAsync(state.Rate, cancellationToken).ConfigureAwait(false);
                await SeekAsync(decision.Position, cancellationToken).ConfigureAwait(false);
                break;
        }

        return new FollowerStatus(snapshot, expected, drift, null);
    }

    /// <summary>
    /// Pushes the applied state to the player if a media file is loaded.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player accepted the commands.</returns>
    private async Task PushToPlayerAsync(CancellationToken cancellationToken)
    {
        var state = Applied;
        var snapshot = _player.GetSnapshot();
        if (state is null || !snapshot.IsLoaded)
        {
            return;
        }

        var now = _clock.NowMicroseconds;
        var scheduled = state.IsScheduledAfter(now);
        var target = state.ExpectedPositionAt(now, snapshot.Duration);
        var drift = snapshot.Position is { } position ? (position - target).Duration() : TimeSpan.MaxValue;

        await _player.SetRateAsync(state.Rate, cancellationToken).ConfigureAwait(false);
        if (state.State == PlayState.Paused || scheduled)
        {
            await _player.SetPausedAsync(true, cancellationToken).ConfigureAwait(false);
            if (drift > PausedTolerance)
            {
                await SeekAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (drift > _corrector.Options.DeadBand)
            {
                await SeekAsync(target, cancellationToken).ConfigureAwait(false);
            }

            await _player.SetPausedAsync(false, cancellationToken).ConfigureAwait(false);
        }

        _appliedToPlayer = true;
    }

    /// <summary>
    /// Checks whether a position is at the end of the loaded media.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="snapshot">The player state.</param>
    /// <returns><see langword="true"/> at the end.</returns>
    private static bool IsAtEnd(TimeSpan position, PlayerSnapshot snapshot)
        => snapshot.Duration is { } duration && duration > TimeSpan.Zero && position >= duration - EndTolerance;

    /// <summary>
    /// Seeks and remembers the time of the seek.
    /// </summary>
    /// <param name="position">The target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the player accepted the command.</returns>
    private async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        _lastSeekAt = _clock.NowMicroseconds;
        await _player.SeekAsync(position, cancellationToken).ConfigureAwait(false);
    }
}
