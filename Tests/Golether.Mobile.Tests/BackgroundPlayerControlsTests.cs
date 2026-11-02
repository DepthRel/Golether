using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Golether.Core.Data.Enums;
using Golether.Localization;
using Golether.Media.Player;
using Golether.Mobile.Services;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of <see cref="BackgroundPlayerControls"/>: the UI thread draws the film, so it must never wait for the player.
/// </summary>
public sealed class BackgroundPlayerControlsTests
{
    /// <summary>
    /// The volume that marks the end of the calls of a test.
    /// </summary>
    private const double SETTLED = 42;

    /// <summary>
    /// How long a test waits for the background; a healthy run takes milliseconds.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The embedded tracks of a film.
    /// </summary>
    private static readonly MediaTrack[] _film = [Track(1, "ru"), Track(2, "en")];

    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player = Substitute.For<ILocalPlayerControls>();

    /// <summary>
    /// Creates the controls over the player.
    /// </summary>
    /// <returns>The controls.</returns>
    private BackgroundPlayerControls Create() => new(_player, NullLogger<BackgroundPlayerControls>.Instance);

    /// <summary>
    /// A selection returns at once even while the player is busy.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task SelectTrack_DoesNotWaitForThePlayer()
    {
        using var release = new ManualResetEventSlim();
        var reached = new TaskCompletionSource();
        _player.When(p => p.SelectTrack(MediaTrackKind.Audio, 2)).Do(_ =>
        {
            release.Wait(_patience);
            reached.TrySetResult();
        });
        var controls = Create();

        controls.SelectTrack(MediaTrackKind.Audio, 2);

        Assert.False(reached.Task.IsCompleted);
        release.Set();
        await reached.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The calls reach the player in the order the user made them.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Calls_KeepTheirOrder()
    {
        var calls = new List<long?>();
        var last = new TaskCompletionSource();
        _player.When(p => p.SelectTrack(MediaTrackKind.Audio, Arg.Any<long?>())).Do(call =>
        {
            calls.Add(call.Arg<long?>());
            if (calls.Count == 20)
            {
                last.TrySetResult();
            }
        });
        var controls = Create();

        for (long id = 1; id <= 20; id++)
        {
            controls.SelectTrack(MediaTrackKind.Audio, id);
        }

        await last.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long?)i), calls);
    }

    /// <summary>
    /// The tracks are read away from the caller when the player reports a change, and the list comes from memory.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task GetTracks_ReturnsWhatTheBackgroundRead()
    {
        var track = new MediaTrack(1, MediaTrackKind.Audio, "ru", "Дубляж", null, null, true, false, false);
        _player.GetTracks().Returns([track]);
        var controls = Create();
        var changed = new TaskCompletionSource();
        controls.TracksChanged += (_, _) => changed.TrySetResult();

        Assert.Empty(controls.GetTracks());
        _player.TracksChanged += Raise.Event();
        await changed.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);

        Assert.Equal([track], controls.GetTracks());
        _player.Received(1).GetTracks();
    }

    /// <summary>
    /// The shape of the film is asked in the background; the caller gets the last known one.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task GetVideoAspect_ReturnsTheLastKnown()
    {
        _player.GetVideoAspect().Returns(1.5);
        var controls = Create();

        var deadline = DateTimeOffset.UtcNow + _patience;
        while (controls.GetVideoAspect() != 1.5 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1.5, controls.GetVideoAspect());
    }

    /// <summary>
    /// A file the player can not take is reported in the words of the user, and the next call still works.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AddSubtitleFile_Failure_IsReported()
    {
        _player.When(p => p.AddSubtitleFile("bad.srt")).Throw(new IOException("gone"));
        var controls = Create();
        var failed = new TaskCompletionSource<string>();
        controls.Failed += (_, text) => failed.TrySetResult(text);
        var next = new TaskCompletionSource();
        _player.When(p => p.SelectTrack(MediaTrackKind.Subtitle, null)).Do(_ => next.TrySetResult());

        controls.AddSubtitleFile("bad.srt");
        controls.SelectTrack(MediaTrackKind.Subtitle, null);

        Assert.Equal(Texts.Get("Tracks.Error.SubtitlesTitle"), await failed.Task.WaitAsync(_patience, TestContext.Current.CancellationToken));
        await next.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A player that went away ends the calls quietly: the session is over, nobody needs the message.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task SelectTrack_OnAClosedPlayer_IsNotReported()
    {
        _player.When(p => p.SelectTrack(MediaTrackKind.Audio, 1)).Throw(new ObjectDisposedException("player"));
        var controls = Create();
        var reported = false;
        controls.Failed += (_, _) => reported = true;
        var next = new TaskCompletionSource();
        _player.When(p => p.SelectTrack(MediaTrackKind.Audio, 2)).Do(_ => next.TrySetResult());

        controls.SelectTrack(MediaTrackKind.Audio, 1);
        controls.SelectTrack(MediaTrackKind.Audio, 2);

        await next.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
        Assert.False(reported);
    }

    /// <summary>
    /// The size of the subtitles reaches a player that can change it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task SubtitleScale_ReachesThePlayer()
    {
        var scaled = (ISubtitleScale)Substitute.For([typeof(ILocalPlayerControls), typeof(ISubtitleScale)], []);
        var reached = new TaskCompletionSource();
        scaled.When(p => p.SetSubtitleScale(1.5)).Do(_ => reached.TrySetResult());
        var controls = new BackgroundPlayerControls((ILocalPlayerControls)scaled, NullLogger<BackgroundPlayerControls>.Instance);

        controls.SetSubtitleScale(1.5);

        await reached.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A file the user added comes back when the same film loads again, as it does after the way back to a session.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AddedFile_ComesBackWithTheSameFilm()
    {
        var controls = await LoadedAsync();
        controls.AddSubtitleFile("own.srt");
        await SettledAsync(controls);
        _player.ClearReceivedCalls();

        await ReloadAsync(controls, _film);

        _player.Received(1).AddSubtitleFile("own.srt");
    }

    /// <summary>
    /// Another film does not get the subtitles of the previous one, and they do not come back later either.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AddedFile_IsDroppedWithAnotherFilm()
    {
        var controls = await LoadedAsync();
        controls.AddAudioFile("own.m4a");
        await SettledAsync(controls);
        _player.ClearReceivedCalls();

        await ReloadAsync(controls, [Track(5, "de")]);
        await ReloadAsync(controls, _film);

        _player.DidNotReceive().AddAudioFile(Arg.Any<string>());
    }

    /// <summary>
    /// A file the player refuses is tried once per load and does not stop the others.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task AddedFile_ThatFails_IsNotRepeatedOnEveryChange()
    {
        var controls = await LoadedAsync();
        controls.AddSubtitleFile("bad.srt");
        controls.AddAudioFile("own.m4a");
        await SettledAsync(controls);
        _player.When(p => p.AddSubtitleFile("bad.srt")).Do(_ => throw new IOException("gone"));
        _player.ClearReceivedCalls();

        await ReloadAsync(controls, _film);
        await ChangeTracksAsync(controls, _film);

        _player.Received(1).AddSubtitleFile("bad.srt");
        _player.Received(1).AddAudioFile("own.m4a");
    }

    /// <summary>
    /// After leaving the copies are gone, so a return to the same film does not ask for them.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task ForgetFiles_KeepsThemFromComingBack()
    {
        var controls = await LoadedAsync();
        controls.AddSubtitleFile("own.srt");
        await SettledAsync(controls);
        controls.ForgetFiles();
        _player.ClearReceivedCalls();

        await ReloadAsync(controls, _film);

        _player.DidNotReceive().AddSubtitleFile(Arg.Any<string>());
    }

    /// <summary>
    /// Describes an embedded sound track.
    /// </summary>
    /// <param name="id">The number of the track.</param>
    /// <param name="language">The language.</param>
    /// <returns>The track.</returns>
    private static MediaTrack Track(long id, string language)
        => new(id, MediaTrackKind.Audio, null, language, null, null, false, false, true);

    /// <summary>
    /// Creates the controls with the film loaded.
    /// </summary>
    /// <returns>The controls.</returns>
    private async Task<BackgroundPlayerControls> LoadedAsync()
    {
        var controls = Create();
        await ChangeTracksAsync(controls, _film);

        return controls;
    }

    /// <summary>
    /// Plays a film going away and another one coming.
    /// </summary>
    /// <param name="controls">The controls.</param>
    /// <param name="film">The tracks of the new film.</param>
    /// <returns>A task that completes when the controls have read them.</returns>
    private async Task ReloadAsync(BackgroundPlayerControls controls, IReadOnlyList<MediaTrack> film)
    {
        await ChangeTracksAsync(controls, []);
        await ChangeTracksAsync(controls, film);
        await SettledAsync(controls);
    }

    /// <summary>
    /// Tells that the tracks changed and waits until they are read.
    /// </summary>
    /// <param name="controls">The controls.</param>
    /// <param name="tracks">The tracks the player has now.</param>
    /// <returns>A task that completes when the controls have read them.</returns>
    private async Task ChangeTracksAsync(BackgroundPlayerControls controls, IReadOnlyList<MediaTrack> tracks)
    {
        _player.GetTracks().Returns(tracks);
        _player.TracksChanged += Raise.Event();
        await SettledAsync(controls);
    }

    /// <summary>
    /// Waits until every call made so far has run.
    /// </summary>
    /// <param name="controls">The controls.</param>
    /// <returns>A task that completes when the queue reached this point.</returns>
    private async Task SettledAsync(BackgroundPlayerControls controls)
    {
        var reached = new TaskCompletionSource();
        _player.When(p => p.SetVolume(SETTLED)).Do(_ => reached.TrySetResult());

        controls.SetVolume(SETTLED);

        await reached.Task.WaitAsync(_patience, TestContext.Current.CancellationToken);
    }
}
