namespace Golether.Core.Playback;

/// <summary>
/// The kind of a drift correction decision.
/// </summary>
public enum DriftAction
{
    /// <summary>
    /// Nothing to do.
    /// </summary>
    None = 0,

    /// <summary>
    /// Change the playback rate to <see cref="DriftDecision.Rate"/>.
    /// </summary>
    AdjustRate = 1,

    /// <summary>
    /// Seek to <see cref="DriftDecision.Position"/>.
    /// </summary>
    Seek = 2,
}
