using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// A capture device.
/// </summary>
/// <param name="Id">The backend identifier.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Kind">The device kind.</param>
public sealed record CaptureDevice(string Id, string Name, CaptureDeviceKind Kind);

/// <summary>
/// The kind of a capture device.
/// </summary>
public enum CaptureDeviceKind
{
    /// <summary>
    /// A camera.
    /// </summary>
    Camera = 0,

    /// <summary>
    /// A microphone.
    /// </summary>
    Microphone = 1,
}

/// <summary>
/// A decoded video frame in BGRA format.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Stride">The number of bytes per row.</param>
/// <param name="Pixels">The pixel data; valid only during the event handler.</param>
public readonly record struct VideoFrame(int Width, int Height, int Stride, ReadOnlyMemory<byte> Pixels);

/// <summary>
/// Handles a data channel message of a participant.
/// </summary>
/// <param name="sender">The backend.</param>
/// <param name="peer">The verified sender.</param>
/// <param name="data">The message, valid only during the call.</param>
public delegate void PeerDataHandler(object? sender, PeerId peer, ReadOnlySpan<byte> data);

/// <summary>
/// A frame of a participant camera.
/// </summary>
/// <param name="PeerId">The participant, or <see langword="default"/> for the local preview.</param>
/// <param name="Frame">The frame.</param>
public readonly record struct ParticipantVideoFrame(PeerId PeerId, VideoFrame Frame);

/// <summary>
/// Signaling data the conferencing backend exchanges with a peer over the authenticated control stream
/// (SDP offers and answers, ICE candidates).
/// </summary>
/// <param name="PeerId">The peer.</param>
/// <param name="Kind">The kind, for example <c>offer</c>, <c>answer</c>, <c>candidate</c>.</param>
/// <param name="Payload">The backend-specific payload.</param>
public sealed record ConferenceSignal(PeerId PeerId, string Kind, string Payload);

/// <summary>
/// Cameras and voices of the participants.
/// </summary>
/// <remarks>
/// Media flows peer to peer (every participant with every other, at most five). The DTLS fingerprints inside the
/// signaling are trusted only because the control stream is already authenticated by the pinned device keys.
/// </remarks>
public interface IConferenceMedia : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the backend works on this system.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the reason when <see cref="IsAvailable"/> is <see langword="false"/>.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Raised when a camera frame arrives (local preview or remote participant).
    /// </summary>
    event EventHandler<ParticipantVideoFrame>? VideoFrameReceived;

    /// <summary>
    /// Raised when the backend needs to send signaling data to a peer.
    /// </summary>
    event EventHandler<ConferenceSignal>? SignalReady;

    /// <summary>
    /// Lists the capture devices.
    /// </summary>
    /// <returns>The devices.</returns>
    IReadOnlyList<CaptureDevice> GetDevices();

    /// <summary>
    /// Starts or changes local capture.
    /// </summary>
    /// <param name="camera">The camera, or <see langword="null"/> to send no video.</param>
    /// <param name="microphone">The microphone, or <see langword="null"/> to send no audio.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when capture runs.</returns>
    Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken);

    /// <summary>
    /// Mutes or unmutes the microphone and the camera.
    /// </summary>
    /// <param name="microphoneMuted">Whether the microphone is muted.</param>
    /// <param name="cameraOff">Whether the camera is off.</param>
    void SetMuted(bool microphoneMuted, bool cameraOff);

    /// <summary>
    /// Raised when a verified participant sends a data message. May be raised on any thread; the data is valid only
    /// during the call.
    /// </summary>
    event PeerDataHandler? DataReceived;

    /// <summary>
    /// Gets the participants whose data channel is open and verified.
    /// </summary>
    IReadOnlyCollection<PeerId> DataPeers { get; }

    /// <summary>
    /// Sends a data message to a participant over the encrypted WebRTC data channel.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="message">The message.</param>
    /// <returns><see langword="false"/> when the channel is not ready or its buffer is full.</returns>
    bool TrySendData(PeerId peer, ReadOnlySpan<byte> message);

    /// <summary>
    /// Raised when a participant starts or stops speaking (<see langword="default"/> peer: this device). May be
    /// raised on any thread.
    /// </summary>
    event EventHandler<SpeakingChange>? SpeakingChanged;

    /// <summary>
    /// Raised when the camera quality sent to a participant changes with their connection. May be raised on any
    /// thread.
    /// </summary>
    event EventHandler<VideoQualityChange>? VideoQualityChanged;

    /// <summary>
    /// Sets the TURN relay used by connections created from now on.
    /// </summary>
    /// <param name="turnServer">The relay, for example <c>turn://user:pass@host:port?transport=tcp</c>, or
    /// <see langword="null"/> for direct connections only.</param>
    void SetRelay(string? turnServer);

    /// <summary>
    /// Sets how loud a participant's voice is played on this device. Other participants are not affected.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="volume">The volume: 0 silent, 1 unchanged, up to <see cref="PcmGain.MaxVolume"/>.</param>
    void SetVoiceVolume(PeerId peer, double volume);

    /// <summary>
    /// Gets a value indicating whether the microphone is muted.
    /// </summary>
    bool MicrophoneMuted { get; }

    /// <summary>
    /// Gets a value indicating whether the camera is off.
    /// </summary>
    bool CameraOff { get; }

    /// <summary>
    /// Raised when <see cref="MicrophoneMuted"/> or <see cref="CameraOff"/> changed, for example because the host
    /// switched them off. May be raised on any thread.
    /// </summary>
    event EventHandler? MuteChanged;

    /// <summary>
    /// Starts media with a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="isOfferer">Whether this side creates the offer (the side with the smaller identifier).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when negotiation started.</returns>
    Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken);

    /// <summary>
    /// Passes signaling data received from a peer.
    /// </summary>
    /// <param name="signal">The signal.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the data was applied.</returns>
    Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// Stops media with a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <returns>A task that completes when the connection is closed.</returns>
    Task DisconnectAsync(PeerId peer);

    /// <summary>
    /// Disconnects all peers and stops capture and playback (the session ended).
    /// </summary>
    /// <returns>A task that completes when everything is stopped.</returns>
    Task StopAsync();
}

/// <summary>
/// The placeholder used while no conferencing backend is available.
/// </summary>
public sealed class UnavailableConferenceMedia : IConferenceMedia
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnavailableConferenceMedia"/> class.
    /// </summary>
    /// <param name="reason">The reason shown to the user.</param>
    public UnavailableConferenceMedia(string reason)
    {
        UnavailableReason = string.IsNullOrWhiteSpace(reason) ? "Камеры и голос недоступны." : reason;
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string? UnavailableReason { get; }

    /// <inheritdoc />
    public event EventHandler<ParticipantVideoFrame>? VideoFrameReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<ConferenceSignal>? SignalReady
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices() => [];

    /// <inheritdoc />
    public Task StartCaptureAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void SetMuted(bool microphoneMuted, bool cameraOff)
    {
        if (MicrophoneMuted == microphoneMuted && CameraOff == cameraOff)
        {
            return;
        }

        MicrophoneMuted = microphoneMuted;
        CameraOff = cameraOff;
        MuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool MicrophoneMuted { get; private set; }

    /// <inheritdoc />
    public bool CameraOff { get; private set; }

    /// <inheritdoc />
    public event EventHandler? MuteChanged;

    /// <inheritdoc />
    public Task ConnectAsync(PeerId peer, bool isOfferer, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task HandleSignalAsync(ConferenceSignal signal, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisconnectAsync(PeerId peer) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public void SetRelay(string? turnServer)
    {
    }

    /// <inheritdoc />
    public void SetVoiceVolume(PeerId peer, double volume)
    {
    }

    /// <inheritdoc />
    public event PeerDataHandler? DataReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PeerId> DataPeers => [];

    /// <inheritdoc />
    public bool TrySendData(PeerId peer, ReadOnlySpan<byte> message) => false;

    /// <inheritdoc />
    public event EventHandler<SpeakingChange>? SpeakingChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<VideoQualityChange>? VideoQualityChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
