using Golether.Core.Identity;

namespace Golether.Media.Conference;

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
    /// Reports the state of every connection with a participant. The diagnostic report uses it to tell apart a
    /// connection that never came up, one that came up but carries nothing, and a camera nobody switched on.
    /// </summary>
    /// <returns>The state of each connection, empty when there are none.</returns>
    IReadOnlyList<ConferencePeerDiagnostics> GetPeerDiagnostics() => [];

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
