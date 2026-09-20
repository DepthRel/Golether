using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Media.Player;
using Golether.UI.Services;
using Golether.UI.ViewModels;
using NSubstitute;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="TracksViewModel"/>: every participant picks their own sound and subtitles.
/// </summary>
public sealed class TracksViewModelTests
{
    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player = Substitute.For<ILocalPlayerControls>();

    /// <summary>
    /// The stored settings.
    /// </summary>
    private readonly Dictionary<string, string> _stored = [];

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// The tracks the player reports.
    /// </summary>
    private List<MediaTrack> _tracks = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TracksViewModelTests"/> class.
    /// </summary>
    public TracksViewModelTests()
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => _stored.GetValueOrDefault(call.ArgAt<string>(0)));
        _settings.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _stored[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
                return Task.CompletedTask;
            });
        _player.GetTracks().Returns(_ => _tracks.ToArray());
        _player.When(p => p.SelectTrack(Arg.Any<MediaTrackKind>(), Arg.Any<long?>())).Do(call =>
        {
            var kind = call.ArgAt<MediaTrackKind>(0);
            var id = call.ArgAt<long?>(1);
            _tracks = _tracks.Select(t => t.Kind == kind ? t with { IsSelected = t.Id == id } : t).ToList();
        });
    }

    /// <summary>
    /// The lists show the tracks with the current choice and an "off" entry for subtitles.
    /// </summary>
    [Fact]
    public void Refresh_ShowsTracks()
    {
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();

        model.Refresh();

        Assert.True(model.HasTracks);
        Assert.Equal(2, model.AudioTracks.Count);
        Assert.True(model.AudioTracks[0].IsSelected);
        Assert.Equal(3, model.SubtitleTracks.Count);
        Assert.Null(model.SubtitleTracks[0].Id);
        Assert.True(model.SubtitleTracks[0].IsSelected);
        Assert.False(model.SubtitlesShown);
    }

    /// <summary>
    /// A choice is applied locally and remembered as a language; the next film starts with it.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Choice_IsRememberedForTheNextFilm()
    {
        _tracks = Film(audioSelected: 1, subtitleSelected: null);
        var model = Create();
        await model.LoadAsync();

        model.AudioTracks[1].SelectCommand.Execute(null);
        model.SubtitleTracks[2].SelectCommand.Execute(null);

        _player.Received().SelectTrack(MediaTrackKind.Audio, 2);
        _player.Received().SelectTrack(MediaTrackKind.Subtitle, 2);
        Assert.Equal("ru", _stored[TracksViewModel.AudioLanguageSetting]);
        Assert.Equal("en", _stored[TracksViewModel.SubtitleLanguageSetting]);
        Assert.True(model.SubtitlesShown);
        Assert.True(model.AudioTracks[1].IsSelected);

        // The next film lists its tracks in another order.
        var next = Create();
        _tracks =
        [
            new MediaTrack(1, MediaTrackKind.Audio, null, "rus", "ac3", 6, false, false, false),
            new MediaTrack(2, MediaTrackKind.Audio, null, "eng", "ac3", 6, true, false, true),
            new MediaTrack(1, MediaTrackKind.Subtitle, null, "eng", "subrip", null, false, false, false),
        ];
        _player.ClearReceivedCalls();
        await next.LoadAsync();

        _player.Received(1).SelectTrack(MediaTrackKind.Audio, 1);
        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, 1);
        Assert.True(next.AudioTracks[0].IsSelected);
        Assert.True(next.SubtitlesShown);

        // Preferences are applied once per film: the user may still change the tracks.
        next.AudioTracks[1].SelectCommand.Execute(null);
        Assert.True(next.AudioTracks[1].IsSelected);
    }

    /// <summary>
    /// "No subtitles" is remembered and switches off the default subtitles of the next film, but a file the user
    /// adds later stays shown.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task SubtitlesOff_IsRemembered()
    {
        _stored[TracksViewModel.SubtitleLanguageSetting] = TracksViewModel.SubtitlesOff;
        _tracks = Film(audioSelected: 1, subtitleSelected: 1);
        var model = Create();

        await model.LoadAsync();

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, null);
        Assert.False(model.SubtitlesShown);

        _tracks = [.. _tracks.Select(t => t with { IsSelected = t.Kind == MediaTrackKind.Audio && t.IsSelected }),
            new MediaTrack(3, MediaTrackKind.Subtitle, null, null, "subrip", null, false, true, true)];
        model.Refresh();

        _player.Received(1).SelectTrack(MediaTrackKind.Subtitle, null);
        Assert.True(model.SubtitlesShown);
    }

    /// <summary>
    /// Subtitles from a file are loaded; a failure is shown to the user.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task AddSubtitles_LoadsTheFile()
    {
        var model = Create();
        _dialogs.PickSubtitleFileAsync().Returns("C:\\films\\dune.srt", "C:\\films\\broken.srt");
        _player.When(p => p.AddSubtitleFile("C:\\films\\broken.srt")).Do(_ => throw new IOException("нет доступа"));

        await model.AddSubtitlesCommand.ExecuteAsync(null);
        await model.AddSubtitlesCommand.ExecuteAsync(null);

        _player.Received(1).AddSubtitleFile("C:\\films\\dune.srt");
        await _dialogs.Received(1).ShowErrorAsync("Субтитры не загружены", "нет доступа");
    }

    /// <summary>
    /// Changes reported by the player on its own thread are applied once through the dispatcher.
    /// </summary>
    [Fact]
    public void PlayerChanges_AreCoalesced()
    {
        var posted = new List<Action>();
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => posted.Add(call.Arg<Action>()));
        var model = new TracksViewModel(_player, _settings, _dialogs, dispatcher);
        _tracks = Film(audioSelected: 1, subtitleSelected: null);

        _player.TracksChanged += Raise.Event();
        _player.TracksChanged += Raise.Event();
        Assert.Single(posted);
        posted[0]();

        Assert.True(model.HasTracks);
        _player.TracksChanged += Raise.Event();
        Assert.Equal(2, posted.Count);
    }

    /// <summary>
    /// Builds the tracks of a film with Russian and English sound and subtitles.
    /// </summary>
    /// <param name="audioSelected">The selected sound track.</param>
    /// <param name="subtitleSelected">The selected subtitles.</param>
    /// <returns>The tracks.</returns>
    private static List<MediaTrack> Film(long audioSelected, long? subtitleSelected) =>
    [
        new MediaTrack(1, MediaTrackKind.Audio, "Original", "eng", "dts", 6, true, false, audioSelected == 1),
        new MediaTrack(2, MediaTrackKind.Audio, "Дубляж", "rus", "ac3", 6, false, false, audioSelected == 2),
        new MediaTrack(1, MediaTrackKind.Subtitle, null, "rus", "subrip", null, true, false, subtitleSelected == 1),
        new MediaTrack(2, MediaTrackKind.Subtitle, null, "eng", "subrip", null, false, false, subtitleSelected == 2),
    ];

    /// <summary>
    /// Creates the model with an immediate dispatcher.
    /// </summary>
    /// <returns>The model.</returns>
    private TracksViewModel Create()
    {
        var dispatcher = Substitute.For<IUiDispatcher>();
        dispatcher.When(d => d.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        return new TracksViewModel(_player, _settings, _dialogs, dispatcher);
    }
}
