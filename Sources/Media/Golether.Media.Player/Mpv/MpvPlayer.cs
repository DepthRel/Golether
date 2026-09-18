using System.Globalization;
using System.Runtime.InteropServices;
using Golether.Core.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// Settings of <see cref="MpvPlayer"/>.
/// </summary>
public sealed record MpvPlayerOptions
{
    /// <summary>
    /// Gets the native window handle to render into (HWND on Windows, X11 window id on Linux); 0 opens mpv's own window.
    /// </summary>
    public nint WindowHandle { get; init; }

    /// <summary>
    /// Gets the directory with the user's <c>mpv.conf</c>, or <see langword="null"/> to ignore user configuration.
    /// </summary>
    public string? ConfigDirectory { get; init; }

    /// <summary>
    /// Gets the hardware decoding mode (default <c>auto-safe</c>).
    /// </summary>
    public string HardwareDecoding { get; init; } = "auto-safe";

    /// <summary>
    /// Gets the demuxer cache size (default <c>512MiB</c>).
    /// </summary>
    public string DemuxerCacheSize { get; init; } = "512MiB";
}

/// <summary>
/// <see cref="IPlaybackController"/> backed by libmpv.
/// </summary>
/// <remarks>
/// Keyboard and mouse bindings and the on-screen controller of mpv are disabled: all user intents go through the
/// Golether UI so they can be synchronized. Observed properties are updated by a dedicated event thread.
/// </remarks>
public sealed class MpvPlayer : IPlaybackController, ILocalPlayerControls
{
    /// <summary>
    /// The first argument of the messages sent by the video input bindings.
    /// </summary>
    private const string PointerMessage = "golether-video";

    /// <summary>
    /// The input section with the mouse bindings of the video picture. Only the left button is bound; mpv's own
    /// bindings stay disabled.
    /// </summary>
    private const string PointerBindings =
        "MBTN_LEFT script-message " + PointerMessage + " click\n" +
        "MBTN_LEFT_DBL script-message " + PointerMessage + " double";

    /// <summary>
    /// Observed property identifiers.
    /// </summary>
    private enum Observed : ulong
    {
        /// <summary>
        /// <c>time-pos</c>.
        /// </summary>
        TimePos = 1,

        /// <summary>
        /// <c>duration</c>.
        /// </summary>
        Duration = 2,

        /// <summary>
        /// <c>pause</c>.
        /// </summary>
        Pause = 3,

        /// <summary>
        /// <c>paused-for-cache</c>.
        /// </summary>
        PausedForCache = 4,

        /// <summary>
        /// <c>demuxer-cache-duration</c>.
        /// </summary>
        CacheDuration = 5,

        /// <summary>
        /// <c>speed</c>.
        /// </summary>
        Speed = 6,

        /// <summary>
        /// <c>seeking</c>.
        /// </summary>
        Seeking = 7,

        /// <summary>
        /// <c>track-list</c> (no data, only the change).
        /// </summary>
        TrackList = 8,

        /// <summary>
        /// <c>aid</c> (no data).
        /// </summary>
        AudioTrack = 9,

        /// <summary>
        /// <c>sid</c> (no data).
        /// </summary>
        SubtitleTrack = 10,

        /// <summary>
        /// <c>demuxer-cache-state</c> (no data; read as JSON on change).
        /// </summary>
        CacheState = 11,
    }

    /// <summary>
    /// The mpv handle.
    /// </summary>
    private readonly nint _handle;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The event thread.
    /// </summary>
    private readonly Thread _eventThread;

    /// <summary>
    /// Guards the snapshot fields.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Whether a file is loaded.
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// The position in seconds.
    /// </summary>
    private double? _position;

    /// <summary>
    /// The duration in seconds.
    /// </summary>
    private double? _duration;

    /// <summary>
    /// The pause flag.
    /// </summary>
    private bool _paused = true;

    /// <summary>
    /// Whether mpv waits for the cache.
    /// </summary>
    private bool _pausedForCache;

    /// <summary>
    /// Whether mpv is seeking.
    /// </summary>
    private bool _seeking;

    /// <summary>
    /// The buffered seconds.
    /// </summary>
    private double _cacheAhead;

    /// <summary>
    /// The playback speed.
    /// </summary>
    private double _speed = 1.0;

    /// <summary>
    /// The cached parts of the media.
    /// </summary>
    private IReadOnlyList<MediaTimeRange> _buffered = [];

    /// <summary>
    /// Whether the player was disposed.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MpvPlayer"/> class.
    /// </summary>
    /// <param name="handle">The initialized mpv handle.</param>
    /// <param name="logger">The logger.</param>
    private MpvPlayer(nint handle, ILogger logger)
    {
        _handle = handle;
        _logger = logger;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _eventThread.Start();
    }

    /// <summary>
    /// Raised on the event thread when a file finished loading or stopped.
    /// </summary>
    public event EventHandler<bool>? LoadedChanged;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public event EventHandler<VideoPointerAction>? VideoPointer;

    /// <inheritdoc />
    public event EventHandler? TracksChanged;

    /// <inheritdoc />
    public void SetVolume(double percent)
    {
        ThrowIfDisposed();
        var value = Math.Clamp(percent, 0, 100);
        LibMpv.Check(LibMpv.SetPropertyDouble(_handle, "volume", MpvFormat.Double, ref value), "setting the volume");
    }

    /// <summary>
    /// Feeds a key into mpv's input system as if the user pressed it (tests).
    /// </summary>
    /// <param name="key">The mpv key name, for example <c>MBTN_LEFT</c>.</param>
    internal void SimulateKey(string key)
    {
        ThrowIfDisposed();
        LibMpv.Check(LibMpv.Command(_handle, "keypress", key), "simulating a key");
    }

    /// <inheritdoc />
    public void SetMuted(bool muted)
    {
        ThrowIfDisposed();
        SetFlag("mute", muted);
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaTrack> GetTracks()
    {
        ThrowIfDisposed();
        return ReadTracks(name => LibMpv.GetString(_handle, name));
    }

    /// <inheritdoc />
    public void SelectTrack(MediaTrackKind kind, long? id)
    {
        ThrowIfDisposed();
        var property = kind == MediaTrackKind.Audio ? "aid" : "sid";
        var value = id?.ToString(CultureInfo.InvariantCulture) ?? "no";
        var result = LibMpv.SetPropertyString(_handle, property, value);
        if (result < 0)
        {
            _logger.LogWarning("Selecting {Property}={Value} failed: {Error}", property, value, LibMpv.Describe(result));
        }
    }

    /// <inheritdoc />
    public void AddSubtitleFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfDisposed();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The subtitle file does not exist.", path);
        }

        LibMpv.Check(LibMpv.Command(_handle, "sub-add", Path.GetFullPath(path), "select"), "adding subtitles");
    }

    /// <inheritdoc />
    public void AddAudioFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfDisposed();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The sound file does not exist.", path);
        }

        LibMpv.Check(LibMpv.Command(_handle, "audio-add", Path.GetFullPath(path), "select"), "adding a sound track");
    }

    /// <summary>
    /// Reads the sound and subtitle tracks from mpv's <c>track-list</c> sub-properties.
    /// </summary>
    /// <param name="read">Reads a property as text, <see langword="null"/> when it is unavailable.</param>
    /// <returns>The tracks.</returns>
    internal static IReadOnlyList<MediaTrack> ReadTracks(Func<string, string?> read)
    {
        if (!int.TryParse(read("track-list/count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            return [];
        }

        var tracks = new List<MediaTrack>();
        for (var i = 0; i < Math.Min(count, 256); i++)
        {
            var prefix = $"track-list/{i}/";
            var kind = read(prefix + "type") switch
            {
                "audio" => MediaTrackKind.Audio,
                "sub" => MediaTrackKind.Subtitle,
                _ => (MediaTrackKind?)null,
            };
            if (kind is null || !long.TryParse(read(prefix + "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            int? channels = int.TryParse(read(prefix + "demux-channel-count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
            tracks.Add(new MediaTrack(
                id,
                kind.Value,
                Blank(read(prefix + "title")),
                Blank(read(prefix + "lang")),
                Blank(read(prefix + "codec")),
                channels,
                read(prefix + "default") == "yes",
                read(prefix + "external") == "yes",
                read(prefix + "selected") == "yes"));
        }

        return tracks;
    }

    /// <summary>
    /// Reads the seekable ranges from the JSON form of <c>demuxer-cache-state</c>.
    /// </summary>
    /// <param name="json">The property value, or <see langword="null"/>.</param>
    /// <returns>The ranges, empty when unknown.</returns>
    internal static IReadOnlyList<MediaTimeRange> ParseCacheRanges(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("seekable-ranges", out var list) || list.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return [];
            }

            var ranges = new List<MediaTimeRange>();
            foreach (var item in list.EnumerateArray().Take(64))
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object
                    && item.TryGetProperty("start", out var start) && start.ValueKind == System.Text.Json.JsonValueKind.Number && start.TryGetDouble(out var from)
                    && item.TryGetProperty("end", out var end) && end.ValueKind == System.Text.Json.JsonValueKind.Number && end.TryGetDouble(out var to)
                    && double.IsFinite(from) && double.IsFinite(to) && to > from && from >= 0 && to < TimeSpan.MaxValue.TotalSeconds)
                {
                    ranges.Add(new MediaTimeRange(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to)));
                }
            }

            return ranges;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Turns empty text into <see langword="null"/>.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The text or <see langword="null"/>.</returns>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Creates a player if libmpv is available.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="player">The player.</param>
    /// <param name="error">The reason when the player could not be created.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool TryCreate(MpvPlayerOptions options, ILogger? logger, out MpvPlayer? player, out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
        player = null;
        if (!MpvLibraryResolver.TryLoad(out error))
        {
            return false;
        }

        var handle = LibMpv.Create();
        if (handle == 0)
        {
            error = "libmpv: mpv_create вернула ошибку.";
            return false;
        }

        try
        {
            Configure(handle, options);
            LibMpv.Check(LibMpv.Initialize(handle), "initialization");
            BindPointer(handle);
            MpvStreamRegistry.Install(handle);
            ObserveProperties(handle);
            LibMpv.Check(LibMpv.RequestLogMessages(handle, "warn"), "log subscription");
            player = new MpvPlayer(handle, logger ?? NullLogger.Instance);
            return true;
        }
        catch (MpvException ex)
        {
            LibMpv.TerminateDestroy(handle);
            error = ex.Message;
            return false;
        }
    }

    /// <inheritdoc />
    public PlayerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new PlayerSnapshot(
                _loaded,
                _position is { } position ? TimeSpan.FromSeconds(position) : null,
                _duration is { } duration ? TimeSpan.FromSeconds(duration) : null,
                _paused,
                _pausedForCache || _seeking,
                TimeSpan.FromSeconds(_cacheAhead),
                _speed) { Buffered = _buffered };
        }
    }

    /// <inheritdoc />
    public Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Scheme is not ("file" or MpvStreamRegistry.Protocol))
        {
            throw new ArgumentException("Only file:// and golether:// sources are allowed.", nameof(source));
        }

        ThrowIfDisposed();
        LibMpv.Check(LibMpv.SetPropertyString(_handle, "start", Seconds(startPosition)), "setting the start position");
        SetFlag("pause", paused);
        var target = source.IsFile ? source.LocalPath : source.OriginalString;
        LibMpv.Check(LibMpv.Command(_handle, "loadfile", target, "replace"), "loading the file");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetFlag("pause", paused);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = LibMpv.Command(_handle, "seek", Seconds(position), "absolute+exact");
        if (result < 0)
        {
            _logger.LogWarning("Seek to {Position} failed: {Error}", position, LibMpv.Describe(result));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetRateAsync(double rate, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rate, 0.25);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rate, 4.0);
        ThrowIfDisposed();
        var value = rate;
        LibMpv.Check(LibMpv.SetPropertyDouble(_handle, "speed", MpvFormat.Double, ref value), "setting the speed");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        LibMpv.Command(_handle, "stop");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            LibMpv.Command(_handle, "quit");
            LibMpv.Wakeup(_handle);
            _eventThread.Join(TimeSpan.FromSeconds(5));
            LibMpv.TerminateDestroy(_handle);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sets the options that must be applied before initialization.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <param name="options">The options.</param>
    private static void Configure(nint handle, MpvPlayerOptions options)
    {
        var settings = new List<(string Name, string Value)>
        {
            ("terminal", "no"),
            ("idle", "yes"),
            ("keep-open", "yes"),
            ("input-default-bindings", "no"),
            ("input-vo-keyboard", "no"),
            ("osc", "no"),
            ("osd-level", "0"),
            ("hwdec", options.HardwareDecoding),
            ("cache", "yes"),
            ("demuxer-max-bytes", options.DemuxerCacheSize),
            ("demuxer-readahead-secs", "30"),
            ("audio-pitch-correction", "yes"),
            ("hr-seek", "yes"),
            ("ytdl", "no"),
        };

        if (options.WindowHandle != 0)
        {
            settings.Add(("wid", options.WindowHandle.ToString(CultureInfo.InvariantCulture)));
            settings.Add(("force-window", "yes"));
        }

        if (options.ConfigDirectory is { } configDirectory && Directory.Exists(configDirectory))
        {
            settings.Add(("config-dir", configDirectory));
            settings.Add(("config", "yes"));
        }
        else
        {
            settings.Add(("config", "no"));
        }

        foreach (var (name, value) in settings)
        {
            LibMpv.Check(LibMpv.SetOptionString(handle, name, value), $"option {name}");
        }
    }

    /// <summary>
    /// Makes mpv report clicks on the video picture: its native window receives the mouse input, not the UI.
    /// </summary>
    /// <param name="handle">The handle.</param>
    private static void BindPointer(nint handle)
    {
        LibMpv.Check(LibMpv.Command(handle, "define-section", "golether-pointer", PointerBindings, "force"), "binding the mouse");
        LibMpv.Check(LibMpv.Command(handle, "enable-section", "golether-pointer"), "enabling the mouse bindings");
    }

    /// <summary>
    /// Subscribes to the properties of the snapshot.
    /// </summary>
    /// <param name="handle">The handle.</param>
    private static void ObserveProperties(nint handle)
    {
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.TimePos, "time-pos", MpvFormat.Double), "observing time-pos");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.Duration, "duration", MpvFormat.Double), "observing duration");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.Pause, "pause", MpvFormat.Flag), "observing pause");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.PausedForCache, "paused-for-cache", MpvFormat.Flag), "observing paused-for-cache");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.CacheDuration, "demuxer-cache-duration", MpvFormat.Double), "observing demuxer-cache-duration");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.Speed, "speed", MpvFormat.Double), "observing speed");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.Seeking, "seeking", MpvFormat.Flag), "observing seeking");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.TrackList, "track-list", MpvFormat.None), "observing track-list");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.AudioTrack, "aid", MpvFormat.None), "observing aid");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.SubtitleTrack, "sid", MpvFormat.None), "observing sid");
        LibMpv.Check(LibMpv.ObserveProperty(handle, (ulong)Observed.CacheState, "demuxer-cache-state", MpvFormat.None), "observing demuxer-cache-state");
    }

    /// <summary>
    /// Formats seconds for mpv.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>The invariant text.</returns>
    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sets a flag property.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value.</param>
    private void SetFlag(string name, bool value)
    {
        var flag = value ? 1 : 0;
        LibMpv.Check(LibMpv.SetPropertyFlag(_handle, name, MpvFormat.Flag, ref flag), $"setting {name}");
    }

    /// <summary>
    /// Throws after disposal.
    /// </summary>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    /// <summary>
    /// Processes mpv events until shutdown.
    /// </summary>
    private unsafe void EventLoop()
    {
        while (true)
        {
            var ev = LibMpv.WaitEvent(_handle, -1);
            switch (ev->EventId)
            {
                case MpvEventId.Shutdown:
                    return;
                case MpvEventId.PropertyChange:
                    OnPropertyChange((Observed)ev->ReplyUserData, (MpvEventProperty*)ev->Data);
                    break;
                case MpvEventId.FileLoaded:
                    SetLoaded(true);
                    break;
                case MpvEventId.EndFile:
                    var end = (MpvEventEndFile*)ev->Data;
                    if (end->Reason == 4)
                    {
                        _logger.LogWarning("mpv could not play the file: {Error}", LibMpv.Describe(end->Error));
                    }

                    // keep-open keeps the file loaded at EOF; any other end unloads it.
                    SetLoaded(false);
                    break;
                case MpvEventId.ClientMessage:
                    OnClientMessage((MpvEventClientMessage*)ev->Data);
                    break;
                case MpvEventId.LogMessage:
                    var log = (MpvEventLogMessage*)ev->Data;
                    _logger.LogWarning("mpv [{Prefix}] {Text}", Marshal.PtrToStringUTF8(log->Prefix), Marshal.PtrToStringUTF8(log->Text)?.TrimEnd());
                    break;
            }
        }
    }

    /// <summary>
    /// Raises <see cref="VideoPointer"/> for the messages of the mouse bindings.
    /// </summary>
    /// <param name="message">The message.</param>
    private unsafe void OnClientMessage(MpvEventClientMessage* message)
    {
        if (message->NumArgs != 2 || Marshal.PtrToStringUTF8((nint)message->Args[0]) != PointerMessage)
        {
            return;
        }

        var action = Marshal.PtrToStringUTF8((nint)message->Args[1]) switch
        {
            "click" => VideoPointerAction.Click,
            "double" => VideoPointerAction.DoubleClick,
            _ => (VideoPointerAction?)null,
        };
        if (action is { } value)
        {
            VideoPointer?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Updates the loaded flag.
    /// </summary>
    /// <param name="loaded">The new value.</param>
    private void SetLoaded(bool loaded)
    {
        lock (_gate)
        {
            _loaded = loaded;
            if (!loaded)
            {
                _position = null;
                _duration = null;
                _buffered = [];
            }
        }

        LoadedChanged?.Invoke(this, loaded);
    }

    /// <summary>
    /// Applies a property change.
    /// </summary>
    /// <param name="id">The property.</param>
    /// <param name="property">The event data.</param>
    private unsafe void OnPropertyChange(Observed id, MpvEventProperty* property)
    {
        if (id == Observed.CacheState)
        {
            var ranges = ParseCacheRanges(LibMpv.GetString(_handle, "demuxer-cache-state"));
            lock (_gate)
            {
                _buffered = ranges;
            }

            return;
        }

        if (id is Observed.TrackList or Observed.AudioTrack or Observed.SubtitleTrack)
        {
            TracksChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var hasValue = property->Format != MpvFormat.None && property->Data != 0;
        var number = hasValue && property->Format == MpvFormat.Double ? *(double*)property->Data : (double?)null;
        var flag = hasValue && property->Format == MpvFormat.Flag && *(int*)property->Data != 0;
        lock (_gate)
        {
            switch (id)
            {
                case Observed.TimePos:
                    _position = number;
                    break;
                case Observed.Duration:
                    _duration = number;
                    break;
                case Observed.Pause:
                    _paused = flag;
                    break;
                case Observed.PausedForCache:
                    _pausedForCache = flag;
                    break;
                case Observed.CacheDuration:
                    _cacheAhead = number ?? 0;
                    break;
                case Observed.Speed:
                    _speed = number ?? 1.0;
                    break;
                case Observed.Seeking:
                    _seeking = flag;
                    break;
            }
        }
    }
}
