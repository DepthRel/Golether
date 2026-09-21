using Golether.Core.Data.Enums;
using Golether.Core.Playback;
using Golether.Core.Time;
using Golether.Localization;
using Golether.Media.Player.Mpv;
using Golether.Media.Player.Simulation;
using Golether.Media.Player;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Services;

/// <summary>
/// The player used by sessions: libmpv rendering into the video area once the native window exists, or the simulated
/// player when libmpv is not available.
/// </summary>
/// <remarks>
/// Sessions keep a reference to this proxy, so the backend can be attached after the session was created.
/// </remarks>
public sealed class PlayerHost : IPlaybackController, ILocalPlayerControls
{
    /// <summary>
    /// The simulated player.
    /// </summary>
    private readonly SimulatedPlayer _simulated = new(StopwatchMonotonicClock.Instance, TimeSpan.FromHours(3));

    /// <summary>
    /// The logger factory.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// The directory with the user's <c>mpv.conf</c>.
    /// </summary>
    private readonly string _configDirectory;

    /// <summary>
    /// The libmpv player, when attached.
    /// </summary>
    private MpvPlayer? _mpv;

    /// <summary>
    /// The native window handle of the video area.
    /// </summary>
    private nint _windowHandle;

    /// <summary>
    /// Whether the video area has reported its window.
    /// </summary>
    private bool _hasWindow;

    /// <summary>
    /// The volume of this device, 0–100.
    /// </summary>
    private double _volume = 100;

    /// <summary>
    /// Whether the sound is off on this device.
    /// </summary>
    private bool _muted;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerHost"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="configDirectory">The directory with the user's <c>mpv.conf</c>.</param>
    public PlayerHost(ILoggerFactory loggerFactory, string configDirectory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _configDirectory = configDirectory;
        _unavailableReason = () => Texts.Get("Player.Notice.AfterWindowOpens");

        // The notice is worded when it is asked for; the window asks again when the language changes.
        Texts.Localizer.LanguageChanged += (_, _) => BackendChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Words the reason libmpv is not used, in the language of the moment.
    /// </summary>
    private Func<string?> _unavailableReason;

    /// <summary>
    /// Raised when the backend changes.
    /// </summary>
    public event EventHandler? BackendChanged;

    /// <inheritdoc />
    public event EventHandler<VideoPointerAction>? VideoPointer;

    /// <inheritdoc />
    public event EventHandler? TracksChanged;

    /// <inheritdoc />
    public bool IsAvailable => _mpv is not null;

    /// <summary>
    /// Gets the reason libmpv is not used, or <see langword="null"/>.
    /// </summary>
    public string? UnavailableReason => _unavailableReason();

    /// <summary>
    /// Gets the active backend.
    /// </summary>
    private IPlaybackController Current => (IPlaybackController?)_mpv ?? _simulated;

    /// <summary>
    /// Creates the libmpv player rendering into a native window.
    /// </summary>
    /// <param name="windowHandle">The native window handle.</param>
    /// <returns><see langword="true"/> when libmpv is used.</returns>
    public bool Attach(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _hasWindow = true;
        if (_mpv is not null)
        {
            return true;
        }

        var options = new MpvPlayerOptions { WindowHandle = windowHandle, ConfigDirectory = _configDirectory };
        if (!MpvPlayer.TryCreate(options, _loggerFactory.CreateLogger<MpvPlayer>(), out var player, out var error))
        {
            _unavailableReason = () => Texts.Format("Player.Notice.SyncWithoutPicture", error);
            BackendChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        player!.VideoPointer += (_, action) => VideoPointer?.Invoke(this, action);
        player.TracksChanged += (_, _) => TracksChanged?.Invoke(this, EventArgs.Empty);
        ApplyAudio(player);
        _mpv = player;
        _unavailableReason = () => null;
        BackendChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Tries to create the libmpv player again, for example after the video component was installed.
    /// </summary>
    /// <returns><see langword="true"/> when libmpv is used.</returns>
    public bool Retry() => _hasWindow && Attach(_windowHandle);

    /// <summary>
    /// Releases the libmpv player before its window is destroyed.
    /// </summary>
    /// <returns>A task that completes when the player is released.</returns>
    public async Task DetachAsync()
    {
        var player = Interlocked.Exchange(ref _mpv, null);
        if (player is not null)
        {
            await player.DisposeAsync().ConfigureAwait(false);
            _unavailableReason = () => Texts.Get("Player.Notice.WindowClosed");
            BackendChanged?.Invoke(this, EventArgs.Empty);
            TracksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void SetVolume(double percent)
    {
        _volume = Math.Clamp(percent, 0, 100);
        if (_mpv is { } player)
        {
            ApplyAudio(player);
        }
    }

    /// <inheritdoc />
    public void SetMuted(bool muted)
    {
        _muted = muted;
        if (_mpv is { } player)
        {
            ApplyAudio(player);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaTrack> GetTracks()
    {
        try
        {
            return _mpv?.GetTracks() ?? [];
        }
        catch (ObjectDisposedException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public void SelectTrack(MediaTrackKind kind, long? id) => _mpv?.SelectTrack(kind, id);

    /// <inheritdoc />
    public void AddSubtitleFile(string path)
    {
        if (_mpv is not { } player)
        {
            throw new InvalidOperationException(Texts.Get("Player.Error.SubtitlesNeedVideo"));
        }

        player.AddSubtitleFile(path);
    }

    /// <inheritdoc />
    public void AddAudioFile(string path)
    {
        if (_mpv is not { } player)
        {
            throw new InvalidOperationException(Texts.Get("Player.Error.AudioNeedsVideo"));
        }

        player.AddAudioFile(path);
    }

    /// <inheritdoc />
    public double GetVideoAspect() => _mpv?.GetVideoAspect() ?? 0;

    /// <inheritdoc />
    public PlayerSnapshot GetSnapshot() => Current.GetSnapshot();

    /// <inheritdoc />
    public Task LoadAsync(Uri source, TimeSpan startPosition, bool paused, CancellationToken cancellationToken = default)
        => Current.LoadAsync(source, startPosition, paused, cancellationToken);

    /// <inheritdoc />
    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default) => Current.SetPausedAsync(paused, cancellationToken);

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Current.SeekAsync(position, cancellationToken);

    /// <inheritdoc />
    public Task SetRateAsync(double rate, CancellationToken cancellationToken = default) => Current.SetRateAsync(rate, cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => Current.StopAsync(cancellationToken);

    /// <summary>
    /// Applies the volume and the mute state of this device to libmpv.
    /// </summary>
    /// <param name="player">The player.</param>
    private void ApplyAudio(MpvPlayer player)
    {
        try
        {
            player.SetVolume(_volume);
            player.SetMuted(_muted);
        }
        catch (Exception ex) when (ex is MpvException or ObjectDisposedException)
        {
            _loggerFactory.CreateLogger<PlayerHost>().LogWarning("Volume not applied: {Error}", ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DetachAsync().ConfigureAwait(false);
        await _simulated.DisposeAsync().ConfigureAwait(false);
    }
}
