using Golether.Core.Playback;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of <see cref="DriftCorrector"/>.
/// </summary>
public sealed class DriftCorrectorTests
{
    /// <summary>
    /// The corrector with default options.
    /// </summary>
    private readonly DriftCorrector _corrector = new();

    /// <summary>
    /// A drift inside the dead band is ignored.
    /// </summary>
    [Fact]
    public void SmallDrift_IsIgnored()
    {
        var decision = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(100.1), 1.0, 1.0);

        Assert.Equal(DriftAction.None, decision.Action);
    }

    /// <summary>
    /// A player behind the session speeds up, a player ahead slows down, within the limit.
    /// </summary>
    [Fact]
    public void MediumDrift_AdjustsRateInTheRightDirection()
    {
        var behind = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(99), 1.0, 1.0);
        var ahead = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(101), 1.0, 1.0);

        Assert.Equal(DriftAction.AdjustRate, behind.Action);
        Assert.InRange(behind.Rate, 1.001, 1.05);
        Assert.Equal(DriftAction.AdjustRate, ahead.Action);
        Assert.InRange(ahead.Rate, 0.95, 0.999);
    }

    /// <summary>
    /// The rate change is limited by <see cref="DriftCorrectionOptions.MaxRateAdjustment"/>.
    /// </summary>
    [Fact]
    public void RateAdjustment_IsClamped()
    {
        var decision = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(98.1), 1.0, 1.0);

        Assert.Equal(1.05, decision.Rate, 6);
    }

    /// <summary>
    /// A large drift causes a seek to the session position.
    /// </summary>
    [Fact]
    public void LargeDrift_Seeks()
    {
        var decision = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(95), 1.0, 1.0);

        Assert.Equal(DriftAction.Seek, decision.Action);
        Assert.Equal(TimeSpan.FromSeconds(100), decision.Position);
        Assert.Equal(TimeSpan.FromSeconds(-5), decision.Drift);
    }

    /// <summary>
    /// A running correction continues inside the dead band and stops below the restore threshold (hysteresis).
    /// </summary>
    [Fact]
    public void RunningCorrection_UsesHysteresis()
    {
        var continuing = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(99.9), 1.0, 1.02);
        var restoring = _corrector.Decide(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(99.98), 1.0, 1.02);

        Assert.Equal(DriftAction.AdjustRate, continuing.Action);
        Assert.NotEqual(1.0, continuing.Rate);
        Assert.Equal(DriftAction.AdjustRate, restoring.Action);
        Assert.Equal(1.0, restoring.Rate);
    }

    /// <summary>
    /// Inconsistent thresholds are rejected.
    /// </summary>
    [Fact]
    public void InvalidOptions_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DriftCorrector(new DriftCorrectionOptions { RestoreThreshold = TimeSpan.FromSeconds(1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DriftCorrector(new DriftCorrectionOptions { SeekThreshold = TimeSpan.FromMilliseconds(100) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DriftCorrector(new DriftCorrectionOptions { MaxRateAdjustment = 0.5 }));
    }
}
