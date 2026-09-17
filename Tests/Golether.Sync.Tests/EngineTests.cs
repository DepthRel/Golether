using Golether.Core.Identity;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Sync.Engine;
using Golether.Sync.Protocol;

namespace Golether.Sync.Tests;

/// <summary>
/// Tests of <see cref="PlaybackAuthority"/> and <see cref="PlaybackFollower"/>.
/// </summary>
public sealed class EngineTests
{
    /// <summary>
    /// The host.
    /// </summary>
    private static readonly PeerId Host = PeerId.Parse(new string('a', 64));

    /// <summary>
    /// A participant.
    /// </summary>
    private static readonly PeerId Guest = PeerId.Parse(new string('b', 64));

    /// <summary>
    /// The clock of the test.
    /// </summary>
    private readonly ManualClock _clock = new() { NowMicroseconds = 10_000_000 };

    /// <summary>
    /// Play schedules a start after the largest round trip plus the lead, and versions increase.
    /// </summary>
    [Fact]
    public void Authority_PlaySchedulesStart()
    {
        var authority = new PlaybackAuthority(Host, _clock);

        var state = authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Play, null), TimeSpan.FromMilliseconds(1200));

        Assert.NotNull(state);
        Assert.Equal(PlayState.Playing, state.State);
        Assert.Equal(1, state.Version);
        Assert.Equal(Guest, state.Origin);
        Assert.Equal(_clock.NowMicroseconds + 2_700_000, state.ReferenceTime);
        Assert.True(state.IsScheduledAfter(_clock.NowMicroseconds));
    }

    /// <summary>
    /// The start delay is capped.
    /// </summary>
    [Fact]
    public void Authority_StartDelayIsCapped()
    {
        var authority = new PlaybackAuthority(Host, _clock);

        var state = authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Play, null), TimeSpan.FromSeconds(60))!;

        Assert.Equal(_clock.NowMicroseconds + 8_000_000, state.ReferenceTime);
    }

    /// <summary>
    /// Pause keeps the frame the participant paused at, so everybody stops on the same frame.
    /// </summary>
    [Fact]
    public void Authority_PauseUsesRequestedFrame()
    {
        var authority = new PlaybackAuthority(Host, _clock);
        authority.Apply(Host, new PlaybackRequest(PlaybackRequestKind.Play, null), TimeSpan.Zero);
        _clock.Advance(TimeSpan.FromMinutes(10));

        var state = authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Pause, TimeSpan.FromSeconds(597)), TimeSpan.Zero)!;

        Assert.Equal(PlayState.Paused, state.State);
        Assert.Equal(TimeSpan.FromSeconds(597), state.Position);
        Assert.Equal(PlaybackCause.Pause, state.Cause);
    }

    /// <summary>
    /// Repeated play or pause without position changes nothing; seeks are clamped to the media.
    /// </summary>
    [Fact]
    public void Authority_IgnoresNoOpsAndClampsSeeks()
    {
        var authority = new PlaybackAuthority(Host, _clock) { Duration = TimeSpan.FromHours(2) };

        Assert.Null(authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Pause, null), TimeSpan.Zero));
        var seek = authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromHours(3)), TimeSpan.Zero)!;
        var back = authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromSeconds(-5)), TimeSpan.Zero)!;

        Assert.Equal(TimeSpan.FromHours(2), seek.Position);
        Assert.Equal(TimeSpan.Zero, back.Position);
        Assert.Equal(PlayState.Paused, back.State);
        Assert.Throws<ArgumentException>(() => authority.Apply(Guest, new PlaybackRequest(PlaybackRequestKind.Seek, null), TimeSpan.Zero));
    }

    /// <summary>
    /// A lagging, buffering participant pauses everybody; playback resumes when all have enough data.
    /// </summary>
    [Fact]
    public void Authority_WaitsForLaggingParticipants()
    {
        var authority = new PlaybackAuthority(Host, _clock);
        authority.Apply(Host, new PlaybackRequest(PlaybackRequestKind.Play, null), TimeSpan.Zero);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var lagging = new ParticipantStatus { PeerId = Guest, IsBuffering = true, Drift = TimeSpan.FromSeconds(-3) };

        var hold = authority.EvaluateBuffering([lagging], TimeSpan.Zero);
        var stillWaiting = authority.EvaluateBuffering([lagging with { CacheAhead = TimeSpan.FromSeconds(2) }], TimeSpan.Zero);
        var resume = authority.EvaluateBuffering([lagging with { IsBuffering = false, CacheAhead = TimeSpan.FromSeconds(6) }], TimeSpan.Zero);

        Assert.NotNull(hold);
        Assert.Equal(PlaybackCause.WaitingForParticipants, hold.Cause);
        Assert.Equal(PlayState.Paused, hold.State);
        Assert.Null(stillWaiting);
        Assert.NotNull(resume);
        Assert.Equal(PlaybackCause.ParticipantsReady, resume.Cause);
        Assert.Equal(PlayState.Playing, resume.State);
        Assert.Equal(hold.Position, resume.Position);
    }

    /// <summary>
    /// With the catch-up policy nobody waits.
    /// </summary>
    [Fact]
    public void Authority_CatchUpPolicyDoesNotPause()
    {
        var authority = new PlaybackAuthority(Host, _clock) { BufferingPolicy = BufferingPolicy.LetLaggardsCatchUp };
        authority.Apply(Host, new PlaybackRequest(PlaybackRequestKind.Play, null), TimeSpan.Zero);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var hold = authority.EvaluateBuffering([new ParticipantStatus { PeerId = Guest, IsBuffering = true, Drift = TimeSpan.FromSeconds(-10) }], TimeSpan.Zero);

        Assert.Null(hold);
    }

    /// <summary>
    /// A scheduled start keeps the player paused until the reference time, then starts it.
    /// </summary>
    [Fact]
    public async Task Follower_StartsOnSchedule()
    {
        var player = new FakePlayer(_clock);
        var follower = new PlaybackFollower(player, _clock, new DriftCorrector());
        var token = TestContext.Current.CancellationToken;
        var state = new PlaybackState
        {
            State = PlayState.Playing,
            Position = TimeSpan.FromSeconds(30),
            ReferenceTime = _clock.NowMicroseconds + 2_000_000,
            Version = 1,
            Origin = Guest,
        };

        await follower.ApplyAsync(state, token);
        var waiting = await follower.TickAsync(token);
        Assert.True(player.Paused);
        Assert.Equal(TimeSpan.FromSeconds(30), player.Position);
        Assert.Equal(TimeSpan.FromSeconds(2), waiting.StartsIn);

        _clock.Advance(TimeSpan.FromSeconds(2));
        await follower.TickAsync(token);

        Assert.False(player.Paused);
        Assert.Contains("play", player.Commands);
    }

    /// <summary>
    /// Older versions are ignored.
    /// </summary>
    [Fact]
    public async Task Follower_IgnoresStaleVersions()
    {
        var player = new FakePlayer(_clock);
        var follower = new PlaybackFollower(player, _clock, new DriftCorrector());
        var token = TestContext.Current.CancellationToken;
        var newer = PlaybackState.Initial(Host, _clock.NowMicroseconds) with { Version = 5, Position = TimeSpan.FromSeconds(10) };

        Assert.True(await follower.ApplyAsync(newer, token));
        Assert.False(await follower.ApplyAsync(newer with { Version = 4, Position = TimeSpan.FromSeconds(99) }, token));
        Assert.Equal(TimeSpan.FromSeconds(10), player.Position);
    }

    /// <summary>
    /// Medium drift changes the rate; large drift seeks; drift is not corrected right after a seek.
    /// </summary>
    [Fact]
    public async Task Follower_CorrectsDrift()
    {
        var player = new FakePlayer(_clock);
        var follower = new PlaybackFollower(player, _clock, new DriftCorrector());
        var token = TestContext.Current.CancellationToken;
        var state = new PlaybackState
        {
            State = PlayState.Playing,
            Position = TimeSpan.FromSeconds(100),
            ReferenceTime = _clock.NowMicroseconds,
            Version = 1,
            Origin = Host,
        };
        await follower.ApplyAsync(state, token);
        _clock.Advance(TimeSpan.FromSeconds(3));

        player.Jump(player.Position - TimeSpan.FromMilliseconds(800));
        await follower.TickAsync(token);
        Assert.True(player.Rate > 1.0);

        player.Jump(player.Position - TimeSpan.FromSeconds(5));
        await follower.TickAsync(token);
        Assert.Contains(player.Commands, c => c.StartsWith("seek", StringComparison.Ordinal));
        Assert.Equal(1.0, player.Rate);

        var commands = player.Commands.Count;
        player.Jump(player.Position - TimeSpan.FromSeconds(1));
        await follower.TickAsync(token);
        Assert.Equal(commands, player.Commands.Count);
    }

    /// <summary>
    /// A state received before the media loaded is applied once the player has the media.
    /// </summary>
    [Fact]
    public async Task Follower_AppliesStateAfterLoad()
    {
        var player = new FakePlayer(_clock) { Loaded = false };
        var follower = new PlaybackFollower(player, _clock, new DriftCorrector());
        var token = TestContext.Current.CancellationToken;
        var state = PlaybackState.Initial(Host, _clock.NowMicroseconds) with { Version = 3, Position = TimeSpan.FromMinutes(40) };

        await follower.ApplyAsync(state, token);
        Assert.Empty(player.Commands);

        player.Loaded = true;
        await follower.TickAsync(token);

        Assert.Equal(TimeSpan.FromMinutes(40), player.Position);
    }

    /// <summary>
    /// A local pause acts at once, a local play waits for the host schedule.
    /// </summary>
    [Fact]
    public async Task Follower_LocalIntents()
    {
        var player = new FakePlayer(_clock) { Paused = false };
        var follower = new PlaybackFollower(player, _clock, new DriftCorrector());
        var token = TestContext.Current.CancellationToken;

        await follower.ApplyLocalIntentAsync(new PlaybackRequest(PlaybackRequestKind.Pause, null), token);
        Assert.True(player.Paused);

        await follower.ApplyLocalIntentAsync(new PlaybackRequest(PlaybackRequestKind.Play, null), token);
        Assert.True(player.Paused);

        await follower.ApplyLocalIntentAsync(new PlaybackRequest(PlaybackRequestKind.Seek, TimeSpan.FromSeconds(12)), token);
        Assert.Equal(TimeSpan.FromSeconds(12), player.Position);
    }
}
