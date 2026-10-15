using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Core.Playback;

namespace Golether.Core.Tests;

/// <summary>
/// Tests of <see cref="PlaybackState"/>.
/// </summary>
public sealed class PlaybackStateTests
{
    /// <summary>
    /// A test host identifier.
    /// </summary>
    private static readonly PeerId _host = PeerId.Parse(new string('1', 64));

    /// <summary>
    /// A paused state keeps its position regardless of time.
    /// </summary>
    [Fact]
    public void ExpectedPosition_Paused_StaysAtPosition()
    {
        var state = PlaybackState.Initial(_host, 1_000_000) with { Position = TimeSpan.FromMinutes(5) };

        Assert.Equal(TimeSpan.FromMinutes(5), state.ExpectedPositionAt(99_000_000));
    }

    /// <summary>
    /// A playing state advances with the session time and the rate.
    /// </summary>
    [Fact]
    public void ExpectedPosition_Playing_AdvancesWithRate()
    {
        var state = new PlaybackState
        {
            State = PlayState.Playing,
            Position = TimeSpan.FromSeconds(10),
            ReferenceTime = 5_000_000,
            Rate = 2.0,
            Version = 1,
            Origin = _host,
        };

        Assert.Equal(TimeSpan.FromSeconds(16), state.ExpectedPositionAt(8_000_000));
    }

    /// <summary>
    /// Before a scheduled start the expected position is the start position.
    /// </summary>
    [Fact]
    public void ScheduledStart_WaitsUntilReferenceTime()
    {
        var state = new PlaybackState
        {
            State = PlayState.Playing,
            Position = TimeSpan.FromSeconds(30),
            ReferenceTime = 10_000_000,
            Version = 2,
            Origin = _host,
        };

        Assert.True(state.IsScheduledAfter(9_000_000));
        Assert.False(state.IsScheduledAfter(10_000_000));
        Assert.Equal(TimeSpan.FromSeconds(30), state.ExpectedPositionAt(9_000_000));
        Assert.Equal(TimeSpan.FromSeconds(31), state.ExpectedPositionAt(11_000_000));
    }

    /// <summary>
    /// A position at the largest value (a participant may ask for any) stays there instead of overflowing.
    /// </summary>
    [Fact]
    public void ExpectedPosition_AtTheLargestValue_DoesNotOverflow()
    {
        var state = new PlaybackState
        {
            State = PlayState.Playing,
            Position = TimeSpan.MaxValue,
            ReferenceTime = 5_000_000,
            Version = 3,
            Origin = _host,
        };

        Assert.Equal(TimeSpan.MaxValue, state.ExpectedPositionAt(8_000_000));
    }

    /// <summary>
    /// A state from the network is followed only with a known mode, a position inside the limit and a rate a player
    /// accepts.
    /// </summary>
    [Fact]
    public void IsValid_RefusesWhatCannotBeFollowed()
    {
        var good = PlaybackState.Initial(_host, 0) with { Position = TimeSpan.FromHours(2) };

        Assert.True(good.IsValid());
        Assert.True((good with { State = PlayState.Playing, Rate = PlaybackState.MAX_RATE }).IsValid());
        Assert.False((good with { State = (PlayState)7 }).IsValid());
        Assert.False((good with { Position = TimeSpan.FromSeconds(-1) }).IsValid());
        Assert.False((good with { Position = TimeSpan.MaxValue }).IsValid());
        Assert.False((good with { Rate = double.NaN }).IsValid());
        Assert.False((good with { Rate = 0 }).IsValid());
        Assert.False((good with { Rate = 50 }).IsValid());
    }
}
