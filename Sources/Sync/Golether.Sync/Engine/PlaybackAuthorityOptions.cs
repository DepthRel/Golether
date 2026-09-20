using Golether.Core.Data.Enums;
using Golether.Core.Session;

namespace Golether.Sync.Engine;

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

    /// <summary>
    /// Gets how long playback waits for participants that are not ready before it starts anyway (default 45 s).
    /// </summary>
    public TimeSpan MaxReadyWait { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Checks whether a participant can play from its position: the file is open, the player does not wait for data,
    /// and enough data is buffered (or the rest of the film, near the end).
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="duration">The media duration, when known.</param>
    /// <returns><see langword="true"/> when the participant is ready.</returns>
    public bool IsReady(ParticipantStatus status, TimeSpan? duration)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.IsBuffering || status.Position is not { } position)
        {
            return false;
        }

        var required = ResumeCacheAhead;
        if (duration is { } total && total - position - TimeSpan.FromSeconds(1) < required)
        {
            required = total - position - TimeSpan.FromSeconds(1);
        }

        return status.CacheAhead >= required;
    }
}
