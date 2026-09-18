using Golether.Core.Data.Stores;
using Golether.UI.Services;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="ResumeTracker"/>.
/// </summary>
public sealed class ResumeTrackerTests
{
    /// <summary>
    /// The media identifier.
    /// </summary>
    private const string Film = "c0ffee";

    /// <summary>
    /// The film duration.
    /// </summary>
    private static readonly TimeSpan Duration = TimeSpan.FromHours(2);

    /// <summary>
    /// The start of the test clock.
    /// </summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 17, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The stored settings.
    /// </summary>
    private readonly Dictionary<string, string> _stored = [];

    /// <summary>
    /// The tracker.
    /// </summary>
    private readonly ResumeTracker _tracker;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeTrackerTests"/> class.
    /// </summary>
    public ResumeTrackerTests()
    {
        var settings = Substitute.For<ISettingsStore>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _stored.GetValueOrDefault(call.ArgAt<string>(0)));
        settings.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _stored[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
            return Task.CompletedTask;
        });
        _tracker = new ResumeTracker(settings);
    }

    /// <summary>
    /// A stopped film is offered from where it stopped; saving is throttled during playback and immediate on pause.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task StoppedFilm_IsOffered()
    {
        Assert.Null(await _tracker.BeginAsync(Film, Duration));

        _tracker.Report(Film, TimeSpan.FromMinutes(30), Duration, playing: true, Start);
        _tracker.Report(Film, TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(5), Duration, playing: true, Start.AddSeconds(5));
        Assert.Equal("1800", _stored[ResumeTracker.SettingPrefix + Film]);

        _tracker.Report(Film, TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(7), Duration, playing: false, Start.AddSeconds(7));
        Assert.Equal("1807", _stored[ResumeTracker.SettingPrefix + Film]);

        Assert.Equal(TimeSpan.FromSeconds(1807), await _tracker.BeginAsync(Film, Duration));
    }

    /// <summary>
    /// The start of a new session (position 0) does not erase the stored stop, and nothing is saved before it is read.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task FreshStart_KeepsTheStoredStop()
    {
        _stored[ResumeTracker.SettingPrefix + Film] = "3600";
        _tracker.Report(Film, TimeSpan.FromMinutes(5), Duration, playing: false, Start);
        Assert.Equal("3600", _stored[ResumeTracker.SettingPrefix + Film]);

        Assert.Equal(TimeSpan.FromHours(1), await _tracker.BeginAsync(Film, Duration));
        _tracker.Report(Film, TimeSpan.Zero, Duration, playing: false, Start);
        _tracker.Report(Film, TimeSpan.FromSeconds(20), Duration, playing: true, Start.AddSeconds(20));
        Assert.Equal("3600", _stored[ResumeTracker.SettingPrefix + Film]);
        _tracker.Report("other", TimeSpan.FromMinutes(10), Duration, playing: false, Start);
        Assert.False(_stored.ContainsKey(ResumeTracker.SettingPrefix + "other"));
    }

    /// <summary>
    /// A film watched to the end is forgotten.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task WatchedFilm_IsForgotten()
    {
        _stored[ResumeTracker.SettingPrefix + Film] = "3600";
        await _tracker.BeginAsync(Film, Duration);

        _tracker.Report(Film, Duration - TimeSpan.FromMinutes(1), Duration, playing: false, Start);

        Assert.Equal("0", _stored[ResumeTracker.SettingPrefix + Film]);
        Assert.Null(await _tracker.BeginAsync(Film, Duration));
    }

    /// <summary>
    /// Positions near either end are not offered.
    /// </summary>
    /// <param name="minutes">The stored position in minutes.</param>
    /// <param name="offered">Whether it is offered for a two-hour film.</param>
    [Theory]
    [InlineData(0.5, false)]
    [InlineData(1, true)]
    [InlineData(116, true)]
    [InlineData(117.5, false)]
    public void Offer_SkipsBothEnds(double minutes, bool offered)
        => Assert.Equal(offered, ResumeTracker.IsWorthOffering(TimeSpan.FromMinutes(minutes), Duration));
}
