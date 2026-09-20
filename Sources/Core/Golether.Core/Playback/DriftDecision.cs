namespace Golether.Core.Playback;

/// <summary>
/// A drift correction decision.
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="Rate">The new rate for <see cref="DriftAction.AdjustRate"/>.</param>
/// <param name="Position">The target position for <see cref="DriftAction.Seek"/>.</param>
/// <param name="Drift">The measured drift: positive when the local player is ahead of the session.</param>
public readonly record struct DriftDecision(DriftAction Action, double Rate, TimeSpan Position, TimeSpan Drift);
