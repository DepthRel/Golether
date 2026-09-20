namespace Golether.Core.Playback;

/// <summary>
/// Decides how to bring the local player back to the session position: ignore small drift, absorb medium drift with a
/// slight rate change (inaudible thanks to pitch correction), seek on large drift.
/// </summary>
public sealed class DriftCorrector
{
    /// <summary>
    /// The minimal rate difference worth sending to the player.
    /// </summary>
    private const double RateEpsilon = 0.001;

    /// <summary>
    /// The validated options.
    /// </summary>
    private readonly DriftCorrectionOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DriftCorrector"/> class.
    /// </summary>
    /// <param name="options">The options; defaults when <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    public DriftCorrector(DriftCorrectionOptions? options = null)
    {
        _options = options ?? new DriftCorrectionOptions();
        _options.Validate();
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public DriftCorrectionOptions Options => _options;

    /// <summary>
    /// Makes a decision for a playing player.
    /// </summary>
    /// <param name="expected">The session position.</param>
    /// <param name="actual">The local player position.</param>
    /// <param name="nominalRate">The session rate.</param>
    /// <param name="currentRate">The current player rate.</param>
    /// <returns>The decision.</returns>
    public DriftDecision Decide(TimeSpan expected, TimeSpan actual, double nominalRate, double currentRate)
    {
        var drift = actual - expected;
        var magnitude = drift.Duration();
        var correcting = Math.Abs(currentRate - nominalRate) > RateEpsilon;

        if (magnitude >= _options.SeekThreshold)
        {
            return new DriftDecision(DriftAction.Seek, nominalRate, expected, drift);
        }

        var limit = correcting ? _options.RestoreThreshold : _options.DeadBand;
        if (magnitude < limit)
        {
            return correcting
                ? new DriftDecision(DriftAction.AdjustRate, nominalRate, expected, drift)
                : new DriftDecision(DriftAction.None, currentRate, expected, drift);
        }

        // Ahead of the session (positive drift) -> slow down; behind -> speed up.
        var adjustment = Math.Clamp(drift / _options.CorrectionHorizon, -_options.MaxRateAdjustment, _options.MaxRateAdjustment);
        var rate = nominalRate * (1 - adjustment);
        return Math.Abs(rate - currentRate) < RateEpsilon
            ? new DriftDecision(DriftAction.None, currentRate, expected, drift)
            : new DriftDecision(DriftAction.AdjustRate, rate, expected, drift);
    }
}
