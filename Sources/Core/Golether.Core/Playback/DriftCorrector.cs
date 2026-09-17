namespace Golether.Core.Playback;

/// <summary>
/// Thresholds of drift correction.
/// </summary>
public sealed record DriftCorrectionOptions
{
    /// <summary>
    /// Gets the drift that is ignored when no correction is running (default 150 ms).
    /// </summary>
    public TimeSpan DeadBand { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Gets the drift below which a running rate correction stops (default 50 ms). Must be less than
    /// <see cref="DeadBand"/> so the correction does not flap around the threshold.
    /// </summary>
    public TimeSpan RestoreThreshold { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Gets the drift from which the player seeks instead of changing the rate (default 2 s).
    /// </summary>
    public TimeSpan SeekThreshold { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the maximum relative rate change (default 0.05, that is ±5 %).
    /// </summary>
    public double MaxRateAdjustment { get; init; } = 0.05;

    /// <summary>
    /// Gets the time in which a drift should be absorbed by the rate change (default 8 s).
    /// </summary>
    public TimeSpan CorrectionHorizon { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range or the thresholds are inconsistent.</exception>
    public void Validate()
    {
        if (RestoreThreshold < TimeSpan.Zero || RestoreThreshold >= DeadBand)
        {
            throw new ArgumentOutOfRangeException(nameof(RestoreThreshold), "RestoreThreshold must be non-negative and less than DeadBand.");
        }

        if (SeekThreshold <= DeadBand)
        {
            throw new ArgumentOutOfRangeException(nameof(SeekThreshold), "SeekThreshold must be greater than DeadBand.");
        }

        if (MaxRateAdjustment is <= 0 or > 0.25)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRateAdjustment), "MaxRateAdjustment must be in (0, 0.25].");
        }

        if (CorrectionHorizon <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CorrectionHorizon), "CorrectionHorizon must be positive.");
        }
    }
}

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

/// <summary>
/// A drift correction decision.
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="Rate">The new rate for <see cref="DriftAction.AdjustRate"/>.</param>
/// <param name="Position">The target position for <see cref="DriftAction.Seek"/>.</param>
/// <param name="Drift">The measured drift: positive when the local player is ahead of the session.</param>
public readonly record struct DriftDecision(DriftAction Action, double Rate, TimeSpan Position, TimeSpan Drift);

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
