using Golether.Components.Installation;
using Golether.Core.Identity;
using Golether.Media.Conference;
using Golether.Media.Conference.GStreamer;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Services;

/// <summary>
/// The conferencing backend used by sessions: GStreamer once its component is available, a placeholder before.
/// </summary>
/// <remarks>
/// Sessions keep this proxy, so the component can be installed while the application runs; the next session uses it.
/// </remarks>
public sealed class ConferenceHost : IConferenceMedia, Golether.UI.ViewModels.ICaptureDeviceSelector
{
    /// <summary>
    /// The logger factory.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// The plugin registry cache file.
    /// </summary>
    private readonly string _registryFile;

    /// <summary>
    /// The active backend.
    /// </summary>
    private IConferenceMedia _current = new UnavailableConferenceMedia("Камеры и голос: установите компонент на стартовом экране.");

    /// <summary>
    /// Whether the microphone is muted; applied to every backend.
    /// </summary>
    private bool _microphoneMuted;

    /// <summary>
    /// Whether the camera is off; applied to every backend.
    /// </summary>
    private bool _cameraOff;

    /// <summary>
    /// Whether a session has started capture.
    /// </summary>
    private bool _captureActive;

    /// <summary>
    /// The relay of the current session; applied to every backend.
    /// </summary>
    private string? _relay;

    /// <summary>
    /// The voice volumes, applied again when the backend changes.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<PeerId, double> _voiceVolumes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConferenceHost"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="registryFile">The plugin registry cache file.</param>
    public ConferenceHost(ILoggerFactory loggerFactory, string registryFile)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _registryFile = registryFile;
    }

    /// <inheritdoc />
    public event EventHandler<ParticipantVideoFrame>? VideoFrameReceived;

    /// <inheritdoc />
    public event EventHandler<ConferenceSignal>? SignalReady;

    /// <summary>
    /// Raised when the backend changed.
    /// </summary>
    public event EventHandler? BackendChanged;

    /// <inheritdoc />
    public bool IsAvailable => _current.IsAvailable;

    /// <inheritdoc />
    public string? UnavailableReason => _current.UnavailableReason;

    /// <summary>
    /// Gets the camera used for capture, or <see langword="null"/> for the system default.
    /// </summary>
    public CaptureDevice? SelectedCamera { get; private set; }

    /// <summary>
    /// Gets the microphone used for capture, or <see langword="null"/> for the system default.
    /// </summary>
    public CaptureDevice? SelectedMicrophone { get; private set; }

    /// <summary>
    /// Chooses the capture devices; a running capture switches to them at once.
    /// </summary>
    /// <param name="camera">The camera, or <see langword="null"/> for the system default.</param>
    /// <param name="microphone">The microphone, or <see langword="null"/> for the system default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when capture uses the devices.</returns>
    public Task SelectDevicesAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken)
    {
        SelectedCamera = camera;
        SelectedMicrophone = microphone;
        return _captureActive && _current.IsAvailable ? _current.StartCaptureAsync(camera, microphone, cancellationToken) : Task.CompletedTask;
    }

    /// <summary>
    /// Switches to GStreamer when the component is available.
    /// </summary>
    /// <param name="status">The state of the conference component.</param>
    /// <returns><see langword="true"/> when cameras and voices work.</returns>
    public bool TryActivate(ComponentStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_current.IsAvailable || !status.IsAvailable)
        {
            return _current.IsAvailable;
        }

        var media = new GStreamerConferenceMedia(
            new GStreamerConferenceOptions
            {
                BundleRoot = status.Source == ComponentSource.System ? null : status.Path,
                RegistryFile = _registryFile,
            },
            _loggerFactory.CreateLogger<GStreamerConferenceMedia>());
        if (!media.IsAvailable)
        {
            _current = new UnavailableConferenceMedia(media.UnavailableReason ?? "GStreamer не запустился.");
            BackendChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        media.VideoFrameReceived += (_, frame) => VideoFrameReceived?.Invoke(this, frame);
        media.SignalReady += (_, signal) => SignalReady?.Invoke(this, signal);
        media.SpeakingChanged += (_, change) => SpeakingChanged?.Invoke(this, change);
        media.VideoQualityChanged += (_, change) => VideoQualityChanged?.Invoke(this, change);
        media.DataReceived += (_, peer, data) => DataReceived?.Invoke(this, peer, data);
        media.SetMuted(_microphoneMuted, _cameraOff);
        media.SetRelay(_relay);
        foreach (var (peer, volume) in _voiceVolumes)
        {
            media.SetVoiceVolume(peer, volume);
        }
        _current = media;
        BackendChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices() => _current.GetDevices();

    /// <inheritdoc />
    public IReadOnlyList<ConferencePeerDiagnostics> GetPeerDiagnostics() => _current.GetPeerDiagnostics();

    /// <inheritdoc />
    public Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken)
    {
        _captureActive = true;
        return _current.StartCaptureAsync(camera ?? SelectedCamera, microphone ?? SelectedMicrophone, cancellationToken);
    }

    /// <inheritdoc />
    public void SetMuted(bool microphoneMuted, bool cameraOff)
    {
        var changed = _microphoneMuted != microphoneMuted || _cameraOff != cameraOff;
        _microphoneMuted = microphoneMuted;
        _cameraOff = cameraOff;
        _current.SetMuted(microphoneMuted, cameraOff);
        if (changed)
        {
            MuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public bool MicrophoneMuted => _microphoneMuted;

    /// <inheritdoc />
    public bool CameraOff => _cameraOff;

    /// <inheritdoc />
    public event EventHandler? MuteChanged;

    /// <inheritdoc />
    public Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken) => _current.ConnectAsync(peer, isOfferer, cancellationToken);

    /// <inheritdoc />
    public Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken) => _current.HandleSignalAsync(signal, cancellationToken);

    /// <inheritdoc />
    public Task DisconnectAsync(PeerId peer) => _current.DisconnectAsync(peer);

    /// <inheritdoc />
    public event EventHandler<SpeakingChange>? SpeakingChanged;

    /// <inheritdoc />
    public event EventHandler<VideoQualityChange>? VideoQualityChanged;

    /// <inheritdoc />
    public event PeerDataHandler? DataReceived;

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> DataPeers => _current.DataPeers;

    /// <inheritdoc />
    public bool TrySendData(PeerId peer, ReadOnlySpan<byte> message) => _current.TrySendData(peer, message);

    /// <inheritdoc />
    public void SetRelay(string? turnServer)
    {
        _relay = turnServer;
        _current.SetRelay(turnServer);
    }

    /// <inheritdoc />
    public void SetVoiceVolume(PeerId peer, double volume)
    {
        _voiceVolumes[peer] = volume;
        _current.SetVoiceVolume(peer, volume);
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        _captureActive = false;
        return _current.StopAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _current.DisposeAsync();
}
