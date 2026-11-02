using Microsoft.Extensions.Logging;
using Golether.Core.Data.Enums;
using Golether.Localization;
using Golether.Media.Player;
using Golether.Media.Player.Mpv;

namespace Golether.Mobile.Services;

/// <summary>
/// The local controls of the player for a client that draws the film on its UI thread. A call that waits for the player
/// there could wait for a frame that only this thread can draw, so every call runs in the background, one at a time,
/// and what is read comes from memory.
/// </summary>
public sealed class BackgroundPlayerControls : ILocalPlayerControls
{
    /// <summary>
    /// The player.
    /// </summary>
    private readonly ILocalPlayerControls _player;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<BackgroundPlayerControls> _logger;

    /// <summary>
    /// Guards the queue.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The last call in the queue.
    /// </summary>
    private Task _queue = Task.CompletedTask;

    /// <summary>
    /// The tracks as they were last read.
    /// </summary>
    private volatile IReadOnlyList<MediaTrack> _tracks = [];

    /// <summary>
    /// The shape of the film as it was last read.
    /// </summary>
    private double _aspect;

    /// <summary>
    /// The files the user added to the film; used by the calls of the queue only. A load of the film forgets them, and
    /// the film loads again on the way back to a session.
    /// </summary>
    private readonly List<(MediaTrackKind Kind, string Path)> _addedFiles = [];

    /// <summary>
    /// The embedded tracks of the film the files were added to.
    /// </summary>
    private string _filesFilm = string.Empty;

    /// <summary>
    /// Whether the film went away, so the next one is new to the player.
    /// </summary>
    private bool _filmGone;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundPlayerControls"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="logger">The logger.</param>
    public BackgroundPlayerControls(ILocalPlayerControls player, ILogger<BackgroundPlayerControls> logger)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _player.TracksChanged += (_, _) => Run(ReadTracks);
    }

    /// <inheritdoc />
    public event EventHandler<VideoPointerAction>? VideoPointer
    {
        add => _player.VideoPointer += value;
        remove => _player.VideoPointer -= value;
    }

    /// <inheritdoc />
    public event EventHandler? TracksChanged;

    /// <summary>
    /// Raised on any thread when a file was not taken, with the text for the user.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <inheritdoc />
    public void SetVolume(double percent) => Run(() => _player.SetVolume(percent));

    /// <inheritdoc />
    public void SetMuted(bool muted) => Run(() => _player.SetMuted(muted));

    /// <inheritdoc />
    public IReadOnlyList<MediaTrack> GetTracks() => _tracks;

    /// <inheritdoc />
    public void SelectTrack(MediaTrackKind kind, long? id) => Run(() => _player.SelectTrack(kind, id));

    /// <inheritdoc />
    public void AddSubtitleFile(string path) => Run(() => AddFile(MediaTrackKind.Subtitle, path), FailureOf(MediaTrackKind.Subtitle));

    /// <inheritdoc />
    public void AddAudioFile(string path) => Run(() => AddFile(MediaTrackKind.Audio, path), FailureOf(MediaTrackKind.Audio));

    /// <summary>
    /// Sets the size of the subtitles, when the player can.
    /// </summary>
    /// <param name="scale">The size, 1 for the normal one.</param>
    public void SetSubtitleScale(double scale)
    {
        if (_player is ISubtitleScale player)
        {
            Run(() => player.SetSubtitleScale(scale));
        }
    }

    /// <summary>
    /// Forgets the added files: the session is over and their copies are gone.
    /// </summary>
    public void ForgetFiles() => Run(_addedFiles.Clear);

    /// <inheritdoc />
    /// <remarks>The answer is the last known shape; the call asks for a fresh one for the next time.</remarks>
    public double GetVideoAspect()
    {
        Run(() => Volatile.Write(ref _aspect, _player.GetVideoAspect()));
        return Volatile.Read(ref _aspect);
    }

    /// <summary>
    /// Reads the tracks and tells that they changed.
    /// </summary>
    private void ReadTracks()
    {
        _tracks = _player.GetTracks();
        RestoreFiles();
        TracksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gives the player the added files when the same film loaded again; another film does not get them.
    /// </summary>
    private void RestoreFiles()
    {
        var film = DescribeFilm();
        if (film.Length == 0)
        {
            _filmGone = true;
            return;
        }

        if (!_filmGone)
        {
            return;
        }

        _filmGone = false;
        if (film != _filesFilm)
        {
            _addedFiles.Clear();
            return;
        }

        foreach (var (kind, path) in _addedFiles)
        {
            Guard(() => Add(kind, path), FailureOf(kind));
        }
    }

    /// <summary>
    /// Adds a file to the film and remembers it.
    /// </summary>
    /// <param name="kind">What the file holds.</param>
    /// <param name="path">The path.</param>
    private void AddFile(MediaTrackKind kind, string path)
    {
        Add(kind, path);

        _filesFilm = DescribeFilm();
        _addedFiles.Add((kind, path));
    }

    /// <summary>
    /// Gives a file to the player.
    /// </summary>
    /// <param name="kind">What the file holds.</param>
    /// <param name="path">The path.</param>
    private void Add(MediaTrackKind kind, string path)
    {
        if (kind == MediaTrackKind.Subtitle)
        {
            _player.AddSubtitleFile(path);
        }
        else
        {
            _player.AddAudioFile(path);
        }
    }

    /// <summary>
    /// Tells a film by its embedded tracks.
    /// </summary>
    /// <returns>The description, empty when no film is loaded.</returns>
    private string DescribeFilm()
        => string.Join('|', _tracks.Where(t => !t.IsExternal).Select(t => $"{t.Kind}:{t.Id}:{t.Language}:{t.Title}"));

    /// <summary>
    /// Finds the text for a file that was not taken.
    /// </summary>
    /// <param name="kind">What the file holds.</param>
    /// <returns>The key of the text.</returns>
    private static string FailureOf(MediaTrackKind kind)
        => kind == MediaTrackKind.Subtitle ? "Tracks.Error.SubtitlesTitle" : "Tracks.Error.AudioTitle";

    /// <summary>
    /// Puts a call at the end of the queue.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="failureKey">The text to show when it fails, or <see langword="null"/> for a call the user would not understand.</param>
    private void Run(Action call, string? failureKey = null)
    {
        lock (_gate)
        {
            _queue = _queue.ContinueWith(_ => Guard(call, failureKey), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Runs a call; a failure ends this call only, the queue goes on.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="failureKey">The text to show when it fails, or <see langword="null"/>.</param>
    private void Guard(Action call, string? failureKey)
    {
        try
        {
            call();
        }
        catch (ObjectDisposedException)
        {
            // The session is over and the player with it: nobody waits for this call.
        }
        catch (Exception ex) when (ex is MpvException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "A call to the player failed");
            if (failureKey is not null)
            {
                Failed?.Invoke(this, Texts.Get(failureKey));
            }
        }
    }
}
