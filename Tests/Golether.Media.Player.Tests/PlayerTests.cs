using Golether.Core.Time;
using Golether.Media.Player.Mpv;
using Golether.Media.Player.Simulation;

namespace Golether.Media.Player.Tests;

/// <summary>
/// Tests of <see cref="SimulatedPlayer"/>, <see cref="MpvStreamRegistry"/> and <see cref="MpvLibraryResolver"/>.
/// </summary>
public sealed class PlayerTests
{
    /// <summary>
    /// The simulated player advances only while playing, honours the rate and clamps to the duration.
    /// </summary>
    [Fact]
    public async Task SimulatedPlayer_AdvancesWithClock()
    {
        var clock = new StepClock();
        var player = new SimulatedPlayer(clock, TimeSpan.FromMinutes(10));
        Assert.False(player.GetSnapshot().IsLoaded);

        await player.LoadAsync(new Uri("golether://media/x"), TimeSpan.FromSeconds(5), paused: true, TestContext.Current.CancellationToken);
        clock.Now += 1_000_000;
        Assert.Equal(TimeSpan.FromSeconds(5), player.GetSnapshot().Position);

        await player.SetPausedAsync(false, TestContext.Current.CancellationToken);
        await player.SetRateAsync(2.0, TestContext.Current.CancellationToken);
        clock.Now += 1_000_000;
        Assert.Equal(TimeSpan.FromSeconds(7), player.GetSnapshot().Position);

        clock.Now += 3_600_000_000;
        Assert.Equal(TimeSpan.FromMinutes(10), player.GetSnapshot().Position);

        await player.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(player.GetSnapshot().IsLoaded);
        Assert.False(player.IsAvailable);
    }

    /// <summary>
    /// Registered streams get unique golether:// URIs that can be removed.
    /// </summary>
    [Fact]
    public void StreamRegistry_IssuesUniqueUris()
    {
        var first = MpvStreamRegistry.Register(() => new MemoryStream());
        var second = new MpvMediaUriRegistry().Register(() => new MemoryStream());

        Assert.NotEqual(first, second);
        Assert.StartsWith(MpvStreamRegistry.UriPrefix, first.OriginalString, StringComparison.Ordinal);
        Assert.Equal(MpvStreamRegistry.Protocol, first.Scheme);
        MpvStreamRegistry.Unregister(first);
        MpvStreamRegistry.Unregister(second);
    }

    /// <summary>
    /// The resolver knows the library names of the current platform.
    /// </summary>
    [Fact]
    public void LibraryResolver_ListsPlatformNames()
    {
        var names = MpvLibraryResolver.CandidateNames;

        Assert.NotEmpty(names);
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("libmpv-2.dll", names);
        }
    }

    /// <summary>
    /// A clock the test sets directly.
    /// </summary>
    private sealed class StepClock : IMonotonicClock
    {
        /// <summary>
        /// Gets or sets the current time in microseconds.
        /// </summary>
        public long Now { get; set; }

        /// <inheritdoc />
        public long NowMicroseconds => Now;
    }
}
