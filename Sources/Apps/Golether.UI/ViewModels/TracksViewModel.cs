using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Localization;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Media.Player;
using Golether.UI.Services;

namespace Golether.UI.ViewModels;

/// <summary>
/// The sound and subtitle tracks chosen on this device. Every participant picks their own: one watches the original,
/// another the dubbing, a third with subtitles. Nothing is sent to others.
/// </summary>
/// <remarks>
/// The last choice is remembered as a language (or "subtitles off") and applied to the next file.
/// </remarks>
public sealed partial class TracksViewModel : ObservableObject
{
    /// <summary>
    /// The setting with the preferred sound language.
    /// </summary>
    public const string AudioLanguageSetting = "player.audioLanguage";

    /// <summary>
    /// The setting with the preferred subtitle language, or <see cref="SubtitlesOff"/>.
    /// </summary>
    public const string SubtitleLanguageSetting = "player.subtitleLanguage";

    /// <summary>
    /// The stored value meaning "no subtitles".
    /// </summary>
    public const string SubtitlesOff = "off";

    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// The embedded tracks the preferences were applied to.
    /// </summary>
    private string _appliedTo = string.Empty;

    /// <summary>
    /// Whether a refresh is already posted.
    /// </summary>
    private int _refreshPosted;

    /// <summary>
    /// The preferred sound language, empty when not chosen.
    /// </summary>
    private string _audioLanguage = string.Empty;

    /// <summary>
    /// The preferred subtitle language, <see cref="SubtitlesOff"/>, or empty when not chosen.
    /// </summary>
    private string _subtitleLanguage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="TracksViewModel"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    public TracksViewModel(ILocalPlayerControls player, ISettingsStore settings, IDialogService dialogs, IUiDispatcher dispatcher)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _player.TracksChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref _refreshPosted, 1) == 0)
            {
                _dispatcher.Post(Refresh);
            }
        };
    }

    /// <summary>
    /// Gets the sound tracks.
    /// </summary>
    public ObservableCollection<TrackOptionViewModel> AudioTracks { get; } = [];

    /// <summary>
    /// Gets the subtitle choices, starting with "off".
    /// </summary>
    public ObservableCollection<TrackOptionViewModel> SubtitleTracks { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a file with tracks is loaded.
    /// </summary>
    [ObservableProperty]
    public partial bool HasTracks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether subtitles are shown now.
    /// </summary>
    [ObservableProperty]
    public partial bool SubtitlesShown { get; set; }

    /// <summary>
    /// Gets or sets the tooltip of the track button.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = Texts.Get("Tracks.Summary");

    /// <summary>
    /// Loads the stored preferences.
    /// </summary>
    /// <returns>A task that completes when they are loaded.</returns>
    public async Task LoadAsync()
    {
        _audioLanguage = await _settings.GetAsync(AudioLanguageSetting, CancellationToken.None).ConfigureAwait(true) ?? string.Empty;
        _subtitleLanguage = await _settings.GetAsync(SubtitleLanguageSetting, CancellationToken.None).ConfigureAwait(true) ?? string.Empty;
        Refresh();
    }

    /// <summary>
    /// Reads the tracks from the player, applies the preferences to a new file and updates the lists.
    /// </summary>
    public void Refresh()
    {
        Interlocked.Exchange(ref _refreshPosted, 0);
        var tracks = _player.GetTracks();
        var embedded = string.Join(
            '|',
            tracks.Where(t => !t.IsExternal).Select(t => $"{t.Kind}:{t.Id}:{t.Language}:{t.Title}"));
        if (embedded.Length > 0 && embedded != _appliedTo)
        {
            _appliedTo = embedded;
            if (ApplyPreferences(tracks))
            {
                tracks = _player.GetTracks();
            }
        }
        else if (embedded.Length == 0)
        {
            _appliedTo = string.Empty;
        }

        Show(tracks);
    }

    /// <summary>
    /// Selects a track on this device and remembers its language.
    /// </summary>
    /// <param name="option">The choice.</param>
    internal void Select(TrackOptionViewModel option)
    {
        try
        {
            _player.SelectTrack(option.Kind, option.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return;
        }

        if (option.Kind == MediaTrackKind.Audio)
        {
            _audioLanguage = MediaTrack.Normalize(option.Track?.Language) ?? string.Empty;
            _ = _settings.SetAsync(AudioLanguageSetting, _audioLanguage, CancellationToken.None);
        }
        else
        {
            _subtitleLanguage = option.Track is null ? SubtitlesOff : MediaTrack.Normalize(option.Track.Language) ?? string.Empty;
            _ = _settings.SetAsync(SubtitleLanguageSetting, _subtitleLanguage, CancellationToken.None);
        }

        // The choice is shown at once; the player confirms it with a track change, which refreshes the lists again.
        Show([.. _player.GetTracks().Select(t => t.Kind == option.Kind ? t with { IsSelected = t.Id == option.Id } : t)]);
    }

    /// <summary>
    /// Loads subtitles from a file on this device.
    /// </summary>
    /// <returns>A task that completes when the file is loaded.</returns>
    [RelayCommand]
    private async Task AddSubtitlesAsync()
    {
        var path = await _dialogs.PickSubtitleFileAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            _player.AddSubtitleFile(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Media.Player.Mpv.MpvException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Tracks.Error.SubtitlesTitle"), ex.Message);
        }
    }

    /// <summary>
    /// Loads a sound track from a file on this device (an external dubbing).
    /// </summary>
    /// <returns>A task that completes when the file is loaded.</returns>
    [RelayCommand]
    private async Task AddAudioAsync()
    {
        var path = await _dialogs.PickAudioFileAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            _player.AddAudioFile(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Media.Player.Mpv.MpvException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Tracks.Error.AudioTitle"), ex.Message);
        }
    }

    /// <summary>
    /// Selects the preferred tracks of a newly loaded file.
    /// </summary>
    /// <param name="tracks">The tracks.</param>
    /// <returns><see langword="true"/> when a track was changed.</returns>
    private bool ApplyPreferences(IReadOnlyList<MediaTrack> tracks)
    {
        var changed = false;
        if (_audioLanguage.Length > 0
            && !tracks.Any(t => t is { Kind: MediaTrackKind.Audio, IsSelected: true } && t.HasLanguage(_audioLanguage))
            && tracks.FirstOrDefault(t => t.Kind == MediaTrackKind.Audio && t.HasLanguage(_audioLanguage)) is { } audio)
        {
            changed |= TrySelect(MediaTrackKind.Audio, audio.Id);
        }

        var subtitles = tracks.Where(t => t.Kind == MediaTrackKind.Subtitle).ToArray();
        if (_subtitleLanguage == SubtitlesOff)
        {
            if (subtitles.Any(t => t.IsSelected))
            {
                changed |= TrySelect(MediaTrackKind.Subtitle, null);
            }
        }
        else if (_subtitleLanguage.Length > 0
            && !subtitles.Any(t => t.IsSelected && t.HasLanguage(_subtitleLanguage))
            && subtitles.FirstOrDefault(t => t.HasLanguage(_subtitleLanguage)) is { } subtitle)
        {
            changed |= TrySelect(MediaTrackKind.Subtitle, subtitle.Id);
        }

        return changed;
    }

    /// <summary>
    /// Selects a track, ignoring a player that went away.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="id">The track.</param>
    /// <returns><see langword="true"/> when the call succeeded.</returns>
    private bool TrySelect(MediaTrackKind kind, long? id)
    {
        try
        {
            _player.SelectTrack(kind, id);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rebuilds the lists.
    /// </summary>
    /// <param name="tracks">The tracks.</param>
    private void Show(IReadOnlyList<MediaTrack> tracks)
    {
        AudioTracks.Clear();
        SubtitleTracks.Clear();
        foreach (var track in tracks.Where(t => t.Kind == MediaTrackKind.Audio))
        {
            AudioTracks.Add(new TrackOptionViewModel(this, track.Kind, track, track.DisplayName, track.IsSelected));
        }

        var subtitles = tracks.Where(t => t.Kind == MediaTrackKind.Subtitle).ToArray();
        var shown = subtitles.FirstOrDefault(t => t.IsSelected);
        SubtitleTracks.Add(new TrackOptionViewModel(this, MediaTrackKind.Subtitle, null, Texts.Get("Tracks.NoSubtitles"), shown is null));
        foreach (var track in subtitles)
        {
            SubtitleTracks.Add(new TrackOptionViewModel(this, track.Kind, track, track.DisplayName, track.IsSelected));
        }

        HasTracks = tracks.Count > 0;
        SubtitlesShown = shown is not null;
        var audio = AudioTracks.FirstOrDefault(t => t.IsSelected);
        Summary = Texts.Format(
            "Tracks.SummaryDetails",
            audio?.Label ?? Texts.Get("Tracks.None"),
            shown?.DisplayName ?? Texts.Get("Tracks.Off"));
    }
}
