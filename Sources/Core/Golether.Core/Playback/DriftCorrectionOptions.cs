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
