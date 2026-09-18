using Golether.UI.Controls;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="MotionSmoother"/>.
/// </summary>
public sealed class MotionSmootherTests
{
    /// <summary>
    /// Between updates the value keeps pace with playback.
    /// </summary>
    [Fact]
    public void AdvancesBetweenUpdates()
    {
        var motion = new MotionSmoother();
        motion.SetTarget(10, 1, now: 0);

        Assert.Equal(10.5, motion.ValueAt(0.5), 6);
        Assert.True(motion.IsMoving(0.5));
    }

    /// <summary>
    /// A seek glides: the value starts at the old position, moves monotonically and arrives after the catch-up time.
    /// </summary>
    [Fact]
    public void SeekGlidesToTheTarget()
    {
        var motion = new MotionSmoother(catchUpSeconds: 0.3);
        motion.SetTarget(10, 0, now: 0);
        motion.SetTarget(70, 0, now: 1);

        Assert.Equal(10, motion.ValueAt(1), 6);
        var previous = 10.0;
        for (var t = 1.05; t < 1.3; t += 0.05)
        {
            var value = motion.ValueAt(t);
            Assert.InRange(value, previous, 70);
            previous = value;
        }

        Assert.Equal(70, motion.ValueAt(1.3), 6);
        Assert.False(motion.IsMoving(1.31));
    }

    /// <summary>
    /// A jump shows the value at once.
    /// </summary>
    [Fact]
    public void JumpShowsTheValueAtOnce()
    {
        var motion = new MotionSmoother();
        motion.SetTarget(10, 0, now: 0);
        motion.SetTarget(40, 0, now: 1, jump: true);

        Assert.Equal(40, motion.ValueAt(1), 6);
        Assert.False(motion.IsMoving(1));
    }

    /// <summary>
    /// The value never leaves its range, even when playback runs past the end.
    /// </summary>
    [Fact]
    public void StaysWithinTheRange()
    {
        var motion = new MotionSmoother { Minimum = 0, Maximum = 100 };
        motion.SetTarget(99, 1, now: 0);

        Assert.Equal(100, motion.ValueAt(5), 6);
    }

    /// <summary>
    /// A small correction during playback is continuous: no step at the moment of the update.
    /// </summary>
    [Fact]
    public void CorrectionIsContinuous()
    {
        var motion = new MotionSmoother();
        motion.SetTarget(10, 1, now: 0);
        var before = motion.ValueAt(0.25);
        motion.SetTarget(10.2, 1, now: 0.25);

        Assert.Equal(before, motion.ValueAt(0.25), 6);
        Assert.Equal(10.2 + 0.3, motion.ValueAt(0.55), 6);
    }
}
